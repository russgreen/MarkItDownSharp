using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Worker;

public sealed class SourceDeletedEventArgs(string sourcePath, string? outputPath, bool outputDeleted) : EventArgs
{
    public string SourcePath { get; } = sourcePath;
    public string? OutputPath { get; } = outputPath;
    public bool OutputDeleted { get; } = outputDeleted;
}

public sealed class FileNotStableException(string path) : IOException($"The file '{path}' did not become stable.");

public sealed class FolderMonitorService : IAsyncDisposable
{
    private readonly MarkItDownService _converter;
    private readonly ConcurrentDictionary<string, ConversionJob> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileFingerprint> _observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _outputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _deleting = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _workers = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationTokenSource _workerLifetime = new();
    private readonly object _pauseGate = new();
    private Channel<ConversionJob>? _queue;
    private FileSystemWatcher? _watcher;
    private Task? _reconciliationTask;
    private TaskCompletionSource? _resumeSignal;
    private Regex[] _ignore = [];
    private string _outputPrefix = string.Empty;
    private bool _started;
    private bool _stopped;

    public FolderMonitorService(MarkItDownService converter)
    {
        _converter = converter;
    }

    public MonitorOptions? Options { get; private set; }

    public bool IsPaused
    {
        get
        {
            lock (_pauseGate)
            {
                return _resumeSignal is not null;
            }
        }
    }

    /// <summary>Files that are queued or being converted.</summary>
    public int PendingCount => _pending.Count;

    public event EventHandler<ConversionJob>? JobCreated;
    public event EventHandler<ConversionJob>? JobChanged;
    public event EventHandler<Exception>? MonitorError;
    public event EventHandler<SourceDeletedEventArgs>? SourceDeleted;
    public event EventHandler? PauseChanged;

    public async Task StartAsync(MonitorOptions options, CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException("The monitor has already been started.");
        }

        Options = options with
        {
            InputDirectory = Path.GetFullPath(options.InputDirectory),
            OutputDirectory = Path.GetFullPath(options.OutputDirectory)
        };
        ValidateOptions(Options);
        Directory.CreateDirectory(Options.OutputDirectory);

        _outputPrefix = Path.TrimEndingDirectorySeparator(Options.OutputDirectory) + Path.DirectorySeparatorChar;
        _ignore = Options.IgnorePatterns.Select(ToRegex).ToArray();
        _queue = Channel.CreateBounded<ConversionJob>(
            new BoundedChannelOptions(Math.Max(1, Options.QueueCapacity)) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });

        _watcher = new FileSystemWatcher(Options.InputDirectory)
        {
            IncludeSubdirectories = Options.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = false
        };
        _watcher.Created += (_, e) => EnqueueAfterDelay(e.FullPath);
        _watcher.Changed += (_, e) => EnqueueAfterDelay(e.FullPath);
        _watcher.Deleted += (_, e) => HandleDeleted(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            HandleDeleted(e.OldFullPath);
            EnqueueAfterDelay(e.FullPath);
        };
        _watcher.Error += (_, e) => MonitorError?.Invoke(this, e.GetException());

        var workerCount = Options.WorkerCount > 0 ? Options.WorkerCount : Math.Clamp(Environment.ProcessorCount, 1, 4);
        for (var i = 0; i < workerCount; i++)
        {
            _workers.Add(Task.Run(ProcessQueueAsync, CancellationToken.None));
        }

        _watcher.EnableRaisingEvents = true;
        _started = true;

        foreach (var file in EnumerateSupportedFiles())
        {
            if (TryGetFingerprint(file, out var fingerprint))
            {
                _observed[file] = fingerprint;
            }
        }

        if (Options.ProcessExistingFiles)
        {
            foreach (var file in EnumerateSupportedFiles())
            {
                await EnqueueAsync(file, cancellationToken, force: true);
            }
        }

        _reconciliationTask = Task.Run(ReconcileLoopAsync, CancellationToken.None);
    }

    /// <summary>Stops queued and future conversions from starting; conversions already running keep going.</summary>
    public void Pause()
    {
        lock (_pauseGate)
        {
            if (_resumeSignal is not null || _stopped)
            {
                return;
            }

            _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        PauseChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        TaskCompletionSource? signal;
        lock (_pauseGate)
        {
            signal = _resumeSignal;
            _resumeSignal = null;
        }

        if (signal is not null)
        {
            signal.TrySetResult();
            PauseChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Cancels every queued and running job but keeps monitoring.</summary>
    public void CancelAll()
    {
        foreach (var job in _pending.Values)
        {
            job.Cancel();
        }
    }

    /// <summary>Cancels the queued or running job for a source file.</summary>
    public bool CancelJob(string sourcePath)
    {
        if (_pending.TryGetValue(Path.GetFullPath(sourcePath), out var job))
        {
            job.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Runs a failed, skipped or cancelled job again, forcing a new conversion even if the source has not changed.
    /// </summary>
    public async Task<bool> RetryAsync(ConversionJob job, CancellationToken cancellationToken = default)
    {
        if (!_started || _stopped || !job.IsFinished || job.State == ConversionJobState.Completed)
        {
            return false;
        }

        if (!File.Exists(job.SourcePath) || !_pending.TryAdd(job.SourcePath, job))
        {
            return false;
        }

        job.Prepare(_workerLifetime.Token);
        JobChanged?.Invoke(this, job);
        if (!await WriteToQueueAsync(job, cancellationToken))
        {
            return false;
        }

        return true;
    }

    /// <summary>Scans the input folder now instead of waiting for the next reconciliation interval.</summary>
    public async Task ReconcileNowAsync(CancellationToken cancellationToken = default)
    {
        if (Options is null)
        {
            return;
        }

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateSupportedFiles())
        {
            present.Add(path);
            if (TryGetFingerprint(path, out var fingerprint) &&
                (!_observed.TryGetValue(path, out var observed) || observed != fingerprint))
            {
                await EnqueueAsync(path, cancellationToken);
            }
        }

        // Sources that vanished while the watcher was not looking (network shares, buffer overflows).
        foreach (var known in _observed.Keys.Concat(_outputs.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (!present.Contains(known) && !_pending.ContainsKey(known) && !File.Exists(known))
            {
                HandleDeleted(known);
            }
        }
    }

    /// <summary>
    /// Stops monitoring. When <paramref name="flushQueue"/> is true, jobs already queued are converted first
    /// (up to <paramref name="timeout"/>); otherwise they are cancelled.
    /// </summary>
    public async Task StopAsync(bool flushQueue = false, TimeSpan? timeout = null)
    {
        lock (_pauseGate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
        }

        // Stop accepting new work.
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }

        await _lifetime.CancelAsync();
        Resume();
        _queue?.Writer.TryComplete();

        if (!flushQueue)
        {
            await _workerLifetime.CancelAsync();
        }

        var workers = Task.WhenAll(_workers);
        try
        {
            if (flushQueue)
            {
                var limit = timeout ?? TimeSpan.FromSeconds(30);
                var finished = await Task.WhenAny(workers, Task.Delay(limit));
                if (finished != workers)
                {
                    await _workerLifetime.CancelAsync();
                }
            }

            await workers;
            if (_reconciliationTask is not null)
            {
                await _reconciliationTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(flushQueue: false);
        _lifetime.Dispose();
        _workerLifetime.Dispose();
    }

    private static void ValidateOptions(MonitorOptions options)
    {
        if (!Directory.Exists(options.InputDirectory))
        {
            throw new DirectoryNotFoundException($"Input directory '{options.InputDirectory}' does not exist.");
        }

        var input = Path.TrimEndingDirectorySeparator(options.InputDirectory);
        var output = Path.TrimEndingDirectorySeparator(options.OutputDirectory);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output directory must be different from the monitored input directory.");
        }

        if (options.QueueCapacity < 1)
        {
            throw new ArgumentException("The queue capacity must be at least 1.");
        }
    }

    private void EnqueueAfterDelay(string path)
    {
        if (IsIgnored(path))
        {
            return;
        }

        var token = _lifetime.Token;
        var debounce = Options!.Debounce;
        _ = Task.Run(async () =>
        {
            try
            {
                if (debounce > TimeSpan.Zero)
                {
                    await Task.Delay(debounce, token);
                }

                await EnqueueAsync(path, token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                MonitorError?.Invoke(this, ex);
            }
        }, CancellationToken.None);
    }

    private async Task EnqueueAsync(string path, CancellationToken cancellationToken, bool force = false)
    {
        if (Options is null || _stopped || !IsSupported(path) || !File.Exists(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!TryGetFingerprint(fullPath, out var fingerprint))
        {
            return;
        }

        if (!force && _observed.TryGetValue(fullPath, out var observed) && observed == fingerprint)
        {
            return;
        }

        if (Options.QueueFullBehavior == QueueFullBehavior.Drop && _queue!.Reader.Count >= Options.QueueCapacity)
        {
            // Forget the file so the next reconciliation pass sees it as new again.
            _observed.TryRemove(fullPath, out _);
            MonitorError?.Invoke(this, new IOException($"The queue is full; '{Path.GetFileName(fullPath)}' will be retried at the next reconciliation."));
            return;
        }

        var job = new ConversionJob(fullPath, GetOutputPath(fullPath));
        if (!_pending.TryAdd(fullPath, job))
        {
            return;
        }

        job.Prepare(_workerLifetime.Token);
        JobCreated?.Invoke(this, job);
        await WriteToQueueAsync(job, cancellationToken);
    }

    private async Task<bool> WriteToQueueAsync(ConversionJob job, CancellationToken cancellationToken)
    {
        try
        {
            if (Options!.QueueFullBehavior == QueueFullBehavior.Drop)
            {
                if (!_queue!.Writer.TryWrite(job))
                {
                    Abandon(job, ConversionJobState.Skipped, "The queue is full; the file will be retried at the next reconciliation.");
                    _observed.TryRemove(job.SourcePath, out _);
                    return false;
                }

                return true;
            }

            await _queue!.Writer.WriteAsync(job, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
        {
            Abandon(job, ConversionJobState.Cancelled, null);
            return false;
        }
    }

    private void Abandon(ConversionJob job, ConversionJobState state, string? error)
    {
        _pending.TryRemove(job.SourcePath, out _);
        job.ReleaseCancellation();
        Finish(job, state, error);
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _queue!.Reader;
        while (await reader.WaitToReadAsync(CancellationToken.None))
        {
            await WaitIfPausedAsync();
            if (!reader.TryRead(out var job))
            {
                continue;
            }

            try
            {
                await RunJobAsync(job);
            }
            catch (Exception ex)
            {
                // A faulty event subscriber must not stop the worker.
                MonitorError?.Invoke(this, ex);
            }
        }
    }

    private async Task WaitIfPausedAsync()
    {
        Task? gate;
        lock (_pauseGate)
        {
            gate = _resumeSignal?.Task;
        }

        if (gate is not null)
        {
            await gate;
        }
    }

    private async Task RunJobAsync(ConversionJob job)
    {
        var options = Options!;
        FileFingerprint? converted = null;
        try
        {
            var token = job.Token;
            token.ThrowIfCancellationRequested();

            for (var attempt = 0; ; attempt++)
            {
                job.Attempts = attempt + 1;
                try
                {
                    Update(job, ConversionJobState.WaitingForFile);
                    await WaitForStableFileAsync(job.SourcePath, options, token);

                    // Remember what was actually converted, so a change made during conversion is noticed later.
                    if (TryGetFingerprint(job.SourcePath, out var fingerprint))
                    {
                        converted = fingerprint;
                    }

                    Update(job, ConversionJobState.Processing);
                    var result = await _converter.ConvertFileAsync(job.SourcePath, token);
                    var written = await WriteOutputAsync(job, result.Markdown, options.CollisionBehavior, token);
                    if (written)
                    {
                        _outputs[job.SourcePath] = job.OutputPath;
                        Finish(job, ConversionJobState.Completed, null);
                    }

                    return;
                }
                catch (Exception ex) when (attempt < options.MaxRetries && IsTransient(ex) && !token.IsCancellationRequested)
                {
                    job.Error = $"Attempt {attempt + 1} failed: {ex.Message}";
                    Update(job, ConversionJobState.Retrying);
                    await Task.Delay(options.RetryDelay * (attempt + 1), token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Finish(job, ConversionJobState.Cancelled, null);
        }
        catch (Exception ex)
        {
            Finish(job, ConversionJobState.Failed, ex.Message);
        }
        finally
        {
            _pending.TryRemove(job.SourcePath, out _);
            job.ReleaseCancellation();
            if (converted is { } fingerprint)
            {
                _observed[job.SourcePath] = fingerprint;
            }
            else if (TryGetFingerprint(job.SourcePath, out var current))
            {
                // The file never became readable; do not retry it until it changes again.
                _observed[job.SourcePath] = current;
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        FileNotStableException => false,
        FileNotFoundException or DirectoryNotFoundException or PathTooLongException or EndOfStreamException => false,
        IOException => true,
        UnauthorizedAccessException => true,
        _ => false
    };

    private async Task<bool> WriteOutputAsync(
        ConversionJob job,
        string markdown,
        CollisionBehavior collisionBehavior,
        CancellationToken cancellationToken)
    {
        var destination = job.OutputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // A retry may rewrite the output this same job created earlier; that is not a collision.
        var ownOutput = _outputs.TryGetValue(job.SourcePath, out var previous) &&
                        string.Equals(previous, destination, StringComparison.OrdinalIgnoreCase) &&
                        job.Attempts > 1;

        if (File.Exists(destination) && !ownOutput)
        {
            if (collisionBehavior == CollisionBehavior.Skip)
            {
                Finish(job, ConversionJobState.Skipped, null);
                return false;
            }

            if (collisionBehavior == CollisionBehavior.Version)
            {
                destination = GetVersionedPath(destination);
                job.SetOutputPath(destination);
            }
        }

        var tempPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, markdown, cancellationToken);
            File.Move(tempPath, destination, collisionBehavior == CollisionBehavior.Overwrite || ownOutput);
            return true;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task ReconcileLoopAsync()
    {
        var interval = Options!.ReconciliationInterval;
        if (interval <= TimeSpan.Zero || interval == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                await ReconcileNowAsync(_lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MonitorError?.Invoke(this, ex);
        }
    }

    private void HandleDeleted(string path)
    {
        if (Options is null)
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var folderPrefix = Path.TrimEndingDirectorySeparator(fullPath) + Path.DirectorySeparatorChar;
        var affected = _observed.Keys.Concat(_outputs.Keys)
            .Where(k => string.Equals(k, fullPath, StringComparison.OrdinalIgnoreCase) || k.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var source in affected)
        {
            if (File.Exists(source))
            {
                continue;
            }

            // The watcher and a reconciliation pass can notice the same deletion at once; only one may report it.
            if (!_deleting.TryAdd(source, 0))
            {
                continue;
            }

            try
            {
                var wasObserved = _observed.TryRemove(source, out _);
                var hadOutput = _outputs.TryRemove(source, out var output);
                if (!wasObserved && !hadOutput)
                {
                    continue;
                }

                if (_pending.TryGetValue(source, out var job))
                {
                    job.Cancel();
                }

                var outputDeleted = false;
                if (output is not null && Options.DeleteOutputWhenSourceDeleted && File.Exists(output))
                {
                    try
                    {
                        File.Delete(output);
                        outputDeleted = true;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        MonitorError?.Invoke(this, ex);
                    }
                }

                SourceDeleted?.Invoke(this, new SourceDeletedEventArgs(source, output, outputDeleted));
            }
            finally
            {
                _deleting.TryRemove(source, out _);
            }
        }
    }

    private IEnumerable<string> EnumerateSupportedFiles()
    {
        var options = Options!;
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubdirectories,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };
        return Directory.EnumerateFiles(options.InputDirectory, "*", enumeration).Where(IsSupported);
    }

    // The output folder may be nested inside the input folder; never treat our own output as input.
    private bool IsSupported(string path)
    {
        if (IsIgnored(path) || IsUnderOutput(path))
        {
            return false;
        }

        return _converter.SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private bool IsUnderOutput(string path) =>
        Path.GetFullPath(path).StartsWith(_outputPrefix, StringComparison.OrdinalIgnoreCase);

    private bool IsIgnored(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length == 0)
        {
            return true;
        }

        foreach (var pattern in _ignore)
        {
            if (pattern.IsMatch(name))
            {
                return true;
            }
        }

        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Regex ToRegex(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private string GetOutputPath(string sourcePath)
    {
        var relativePath = Path.GetRelativePath(Options!.InputDirectory, sourcePath);
        var withoutExtension = Path.ChangeExtension(relativePath, null);
        return Path.Combine(Options.OutputDirectory, withoutExtension + ".md");
    }

    /// <summary>
    /// A file is complete when it can be opened exclusively (no writer still holds it) and its size and
    /// timestamp stay the same across consecutive checks. This does not rely on the metadata of the first write.
    /// </summary>
    private static async Task WaitForStableFileAsync(string path, MonitorOptions options, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + options.StabilityTimeout;
        long lastLength = -1;
        DateTime lastWrite = default;
        var stableChecks = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                var length = stream.Length;
                var write = File.GetLastWriteTimeUtc(path);
                if (length == lastLength && write == lastWrite)
                {
                    if (++stableChecks >= options.StableChecksRequired)
                    {
                        return;
                    }
                }
                else
                {
                    stableChecks = 1;
                    lastLength = length;
                    lastWrite = write;
                    if (options.StableChecksRequired <= 1)
                    {
                        return;
                    }
                }
            }
            catch (IOException) when (File.Exists(path))
            {
                // Still locked by the writer.
                stableChecks = 0;
                lastLength = -1;
            }
            catch (UnauthorizedAccessException) when (File.Exists(path))
            {
                stableChecks = 0;
                lastLength = -1;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new FileNotStableException(path);
            }

            await Task.Delay(options.StabilityInterval, cancellationToken);
        }
    }

    private static string GetVersionedPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var version = 1; ; version++)
        {
            var candidate = Path.Combine(directory, $"{name} ({version}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool TryGetFingerprint(string path, out FileFingerprint fingerprint)
    {
        try
        {
            var info = new FileInfo(path);
            fingerprint = new FileFingerprint(info.Length, info.LastWriteTimeUtc.Ticks);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            fingerprint = default;
            return false;
        }
    }

    private void Finish(ConversionJob job, ConversionJobState state, string? error)
    {
        job.Error = error;
        Update(job, state);
    }

    private void Update(ConversionJob job, ConversionJobState state)
    {
        job.State = state;
        JobChanged?.Invoke(this, job);
    }

    private readonly record struct FileFingerprint(long Length, long LastWriteTicks);
}

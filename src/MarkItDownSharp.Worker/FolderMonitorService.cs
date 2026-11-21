using System.Collections.Concurrent;
using System.Threading.Channels;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Worker;

public sealed class FolderMonitorService : IAsyncDisposable
{
    private readonly MarkItDownService _converter;
    private readonly Channel<ConversionJob> _queue = Channel.CreateBounded<ConversionJob>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileFingerprint> _observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _workers = [];
    private readonly CancellationTokenSource _lifetime = new();
    private FileSystemWatcher? _watcher;
    private Task? _reconciliationTask;
    private bool _started;

    public FolderMonitorService(MarkItDownService converter)
    {
        _converter = converter;
    }

    public MonitorOptions? Options { get; private set; }
    public event EventHandler<ConversionJob>? JobCreated;
    public event EventHandler<ConversionJob>? JobChanged;
    public event EventHandler<Exception>? MonitorError;

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

        _watcher = new FileSystemWatcher(Options.InputDirectory)
        {
            IncludeSubdirectories = Options.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = false
        };
        _watcher.Created += (_, e) => EnqueueAfterDelay(e.FullPath, _lifetime.Token);
        _watcher.Changed += (_, e) => EnqueueAfterDelay(e.FullPath, _lifetime.Token);
        _watcher.Renamed += (_, e) => EnqueueAfterDelay(e.FullPath, _lifetime.Token);
        _watcher.Error += (_, e) => MonitorError?.Invoke(this, e.GetException());

        for (var i = 0; i < Math.Clamp(Environment.ProcessorCount, 1, 4); i++)
        {
            _workers.Add(ProcessQueueAsync());
        }

        _watcher.EnableRaisingEvents = true;
        _started = true;

        foreach (var file in EnumerateSupportedFiles(Options.InputDirectory, Options.IncludeSubdirectories))
        {
            if (TryGetFingerprint(file, out var fingerprint))
            {
                _observed[file] = fingerprint;
            }
        }

        if (Options.ProcessExistingFiles)
        {
            foreach (var file in EnumerateSupportedFiles(Options.InputDirectory, Options.IncludeSubdirectories))
            {
                await EnqueueAsync(file, cancellationToken, force: true);
            }
        }

        _reconciliationTask = ReconcileAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }

        _queue.Writer.TryComplete();
        var tasks = _workers.ToArray();
        if (_reconciliationTask is not null)
        {
            tasks = [.. tasks, _reconciliationTask];
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void ValidateOptions(MonitorOptions options)
    {
        if (!Directory.Exists(options.InputDirectory))
        {
            throw new DirectoryNotFoundException($"Input directory '{options.InputDirectory}' does not exist.");
        }

        var inputWithSeparator = Path.TrimEndingDirectorySeparator(options.InputDirectory) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(options.OutputDirectory + Path.DirectorySeparatorChar)
            .StartsWith(inputWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output directory cannot be inside the monitored input directory.");
        }
    }

    private void EnqueueAfterDelay(string path, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cancellationToken);
                await EnqueueAsync(path, cancellationToken);
            }
            catch (OperationCanceledException)
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
        if (Options is null || !IsSupported(path) || !File.Exists(path))
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

        if (!_pending.TryAdd(fullPath, 0))
        {
            return;
        }

        var job = new ConversionJob(fullPath, GetOutputPath(fullPath));
        JobCreated?.Invoke(this, job);
        await _queue.Writer.WriteAsync(job, cancellationToken);
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(_lifetime.Token))
        {
            try
            {
                Update(job, ConversionJobState.WaitingForFile);
                await WaitForStableFileAsync(job.SourcePath, _lifetime.Token);
                Update(job, ConversionJobState.Processing);
                var result = await _converter.ConvertFileAsync(job.SourcePath, _lifetime.Token);
                var completed = await WriteOutputAsync(job, result.Markdown, Options!.CollisionBehavior, _lifetime.Token);
                if (completed)
                {
                    Update(job, ConversionJobState.Completed);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                Update(job, ConversionJobState.Skipped);
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                Update(job, ConversionJobState.Failed);
            }
            finally
            {
                _pending.TryRemove(job.SourcePath, out _);
                if (TryGetFingerprint(job.SourcePath, out var fingerprint))
                {
                    _observed[job.SourcePath] = fingerprint;
                }
            }
        }
    }

    private async Task<bool> WriteOutputAsync(
        ConversionJob job,
        string markdown,
        CollisionBehavior collisionBehavior,
        CancellationToken cancellationToken)
    {
        var destination = job.OutputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            if (collisionBehavior == CollisionBehavior.Skip)
            {
                Update(job, ConversionJobState.Skipped);
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
            File.Move(tempPath, destination, collisionBehavior == CollisionBehavior.Overwrite);
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

    private async Task ReconcileAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token) && Options is not null)
            {
                foreach (var path in EnumerateSupportedFiles(Options.InputDirectory, Options.IncludeSubdirectories))
                {
                    if (TryGetFingerprint(path, out var fingerprint) &&
                        (!_observed.TryGetValue(path, out var observed) || observed != fingerprint))
                    {
                        await EnqueueAsync(path, _lifetime.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private IEnumerable<string> EnumerateSupportedFiles(string directory, bool recurse)
    {
        var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(directory, "*", option).Where(IsSupported);
    }

    private bool IsSupported(string path) =>
        _converter.SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private string GetOutputPath(string sourcePath)
    {
        var relativePath = Path.GetRelativePath(Options!.InputDirectory, sourcePath);
        var withoutExtension = Path.ChangeExtension(relativePath, null);
        return Path.Combine(Options.OutputDirectory, withoutExtension + ".md");
    }

    private static async Task WaitForStableFileAsync(string path, CancellationToken cancellationToken)
    {
        const int attempts = 12;
        long lastLength = -1;
        DateTime lastWrite = default;
        var stableChecks = 0;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var info = new FileInfo(path);
                if (stream.Length == lastLength && info.LastWriteTimeUtc == lastWrite)
                {
                    stableChecks++;
                    if (stableChecks >= 2)
                    {
                        return;
                    }
                }
                else
                {
                    stableChecks = 0;
                    lastLength = stream.Length;
                    lastWrite = info.LastWriteTimeUtc;
                }
            }
            catch (IOException)
            {
                stableChecks = 0;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new IOException($"The file '{path}' did not become stable.");
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
        catch (IOException)
        {
            fingerprint = default;
            return false;
        }
    }

    private void Update(ConversionJob job, ConversionJobState state)
    {
        job.State = state;
        JobChanged?.Invoke(this, job);
    }

    private readonly record struct FileFingerprint(long Length, long LastWriteTicks);
}

using System.ComponentModel;

namespace MarkItDownSharp.Worker;

public sealed class ConversionJob : INotifyPropertyChanged
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private ConversionJobState _state;
    private string? _error;
    private int _attempts;
    private DateTimeOffset? _completedAt;

    public ConversionJob(string sourcePath, string outputPath)
    {
        SourcePath = sourcePath;
        OutputPath = outputPath;
        QueuedAt = DateTimeOffset.Now;
    }

    private ConversionJob(string sourcePath, string outputPath, DateTimeOffset queuedAt)
        : this(sourcePath, outputPath)
    {
        QueuedAt = queuedAt;
    }

    public string SourcePath { get; }
    public string OutputPath { get; private set; }
    public string FileName => Path.GetFileName(SourcePath);
    public DateTimeOffset QueuedAt { get; private set; }

    /// <summary>When the job reached a final state (completed, failed, skipped or cancelled).</summary>
    public DateTimeOffset? CompletedAt
    {
        get => _completedAt;
        private set
        {
            _completedAt = value;
            Raise(nameof(CompletedAt));
        }
    }

    /// <summary>How many conversion attempts have started for this job.</summary>
    public int Attempts
    {
        get => _attempts;
        internal set
        {
            if (_attempts == value)
            {
                return;
            }

            _attempts = value;
            Raise(nameof(Attempts));
        }
    }

    public ConversionJobState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            if (IsFinal(value))
            {
                CompletedAt = DateTimeOffset.Now;
            }

            Raise(nameof(State));
        }
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (_error == value)
            {
                return;
            }

            _error = value;
            Raise(nameof(Error));
        }
    }

    public bool IsFinished => IsFinal(State);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Rebuilds a job from persisted history. Restored jobs are not queued; use
    /// <see cref="FolderMonitorService.RetryAsync"/> to run one again.
    /// </summary>
    public static ConversionJob Restore(
        string sourcePath,
        string outputPath,
        ConversionJobState state,
        string? error,
        DateTimeOffset queuedAt,
        DateTimeOffset? completedAt)
    {
        var job = new ConversionJob(sourcePath, outputPath, queuedAt)
        {
            _state = state,
            _error = error,
            _completedAt = completedAt
        };
        return job;
    }

    /// <summary>
    /// Cancels this job. A queued job is skipped by the worker; a running job is interrupted.
    /// </summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
        }
    }

    internal void SetOutputPath(string outputPath)
    {
        OutputPath = outputPath;
        Raise(nameof(OutputPath));
    }

    /// <summary>Prepares the job for (re)queueing with a fresh cancellation scope linked to the monitor.</summary>
    internal void Prepare(CancellationToken monitorToken)
    {
        lock (_gate)
        {
            _cancellation?.Dispose();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(monitorToken);
        }

        Error = null;
        _completedAt = null;
        Attempts = 0;
        QueuedAt = DateTimeOffset.Now;
        State = ConversionJobState.Queued;
    }

    internal CancellationToken Token
    {
        get
        {
            lock (_gate)
            {
                return _cancellation?.Token ?? CancellationToken.None;
            }
        }
    }

    internal bool IsCancellationRequested
    {
        get
        {
            lock (_gate)
            {
                return _cancellation?.IsCancellationRequested ?? false;
            }
        }
    }

    internal void ReleaseCancellation()
    {
        lock (_gate)
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private static bool IsFinal(ConversionJobState state) =>
        state is ConversionJobState.Completed or ConversionJobState.Failed or ConversionJobState.Skipped or ConversionJobState.Cancelled;

    private void Raise(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

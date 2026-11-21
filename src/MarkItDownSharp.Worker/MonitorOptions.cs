namespace MarkItDownSharp.Worker;

public enum QueueFullBehavior
{
    /// <summary>Detected files wait for room in the queue.</summary>
    Wait,

    /// <summary>Detected files are dropped and picked up again by the next reconciliation pass.</summary>
    Drop
}

public sealed record MonitorOptions(
    string InputDirectory,
    string OutputDirectory,
    bool IncludeSubdirectories = true,
    bool ProcessExistingFiles = false,
    CollisionBehavior CollisionBehavior = CollisionBehavior.Version)
{
    /// <summary>File names that are never converted: editor and download temporaries, lock files and system files.</summary>
    public static readonly IReadOnlyList<string> DefaultIgnorePatterns =
    [
        "~$*", ".~lock.*", "*.tmp", "*.temp", "*.part", "*.partial", "*.crdownload", "*.download", "*.swp", "*~",
        "Thumbs.db", "desktop.ini", ".DS_Store"
    ];

    /// <summary>How long a file must be quiet after a change event before it is queued.</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How often the folder is rescanned for events the watcher missed. Use <see cref="Timeout.InfiniteTimeSpan"/> to disable.</summary>
    public TimeSpan ReconciliationInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Delay between file stability checks.</summary>
    public TimeSpan StabilityInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Consecutive unchanged, exclusively-openable checks needed before a file counts as complete.</summary>
    public int StableChecksRequired { get; init; } = 2;

    /// <summary>Give up waiting for a file to become stable after this long.</summary>
    public TimeSpan StabilityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int QueueCapacity { get; init; } = 1000;

    public QueueFullBehavior QueueFullBehavior { get; init; } = QueueFullBehavior.Wait;

    /// <summary>Number of parallel conversions. Zero chooses a value from the processor count.</summary>
    public int WorkerCount { get; init; }

    /// <summary>Extra attempts after a transient I/O failure such as a file lock.</summary>
    public int MaxRetries { get; init; } = 3;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public IReadOnlyList<string> IgnorePatterns { get; init; } = DefaultIgnorePatterns;

    /// <summary>Delete the generated Markdown when its source file is deleted.</summary>
    public bool DeleteOutputWhenSourceDeleted { get; init; }
}

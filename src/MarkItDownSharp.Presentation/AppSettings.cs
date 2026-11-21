using MarkItDownSharp.Converters;
using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Presentation;

public enum JobNotificationMode
{
    None,
    FailuresOnly,
    All
}

public enum AppTheme
{
    System,
    Light,
    Dark
}

public sealed class AppSettings
{
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public bool IncludeSubdirectories { get; set; } = true;
    public bool ProcessExistingFiles { get; set; }
    public CollisionBehavior CollisionBehavior { get; set; } = CollisionBehavior.Version;

    public int DebounceMilliseconds { get; set; } = 500;
    public int ReconciliationSeconds { get; set; } = 15;
    public int QueueCapacity { get; set; } = 1000;
    public QueueFullBehavior QueueFullBehavior { get; set; } = QueueFullBehavior.Wait;
    public int MaxRetries { get; set; } = 3;
    public bool DeleteOutputWhenSourceDeleted { get; set; }

    public JobNotificationMode JobNotifications { get; set; } = JobNotificationMode.FailuresOnly;
    public bool NotifyApplicationEvents { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool StartWithWindows { get; set; }
    public bool StartMonitoringOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool HasSeenWelcome { get; set; }

    /// <summary>Recognise text in images embedded in Office documents and PDFs, and in image files (slower).</summary>
    public bool EnableOcr { get; set; }

    public ConverterSettings Converters { get; set; } = new();

    /// <summary>Protected API keys, keyed by integration. Written and read by <see cref="SettingsStore"/> only.</summary>
    public Dictionary<string, string> ProtectedSecrets { get; set; } = [];
}

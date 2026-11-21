using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkItDownSharp.Converters;
using MarkItDownSharp.Core;
using MarkItDownSharp.Core.Plugins;
using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Presentation;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxToastsPerWindow = 5;
    private static readonly TimeSpan ToastWindow = TimeSpan.FromSeconds(30);

    private readonly IUiDispatcher _dispatcher;
    private readonly IShellServices _shell;
    private readonly SettingsStore _settingsStore;
    private readonly JobHistoryStore _historyStore;
    private readonly IOcrEngine? _ocr;
    private readonly Dictionary<string, JobItemViewModel> _jobModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _historySaveLock = new();
    private Task _historySaveChain = Task.CompletedTask;
    private readonly Queue<DateTimeOffset> _recentToasts = new();
    private AppSettings _settings = new();
    private FolderMonitorService? _monitor;
    private ConverterCatalogResult? _catalog;
    private bool _loading;
    private bool _toastSummarySent;

    public MainViewModel(
        IUiDispatcher dispatcher,
        IShellServices shell,
        AppPaths? paths = null,
        ISecretProtector? protector = null,
        IOcrEngine? ocr = null)
    {
        paths ??= AppPaths.Default;
        _dispatcher = dispatcher;
        _shell = shell;
        _ocr = ocr;
        _settingsStore = new SettingsStore(paths, protector);
        _historyStore = new JobHistoryStore(paths);
        Paths = paths;
        CrashReporter = new CrashReporter(paths);
    }

    public AppPaths Paths { get; }
    public CrashReporter CrashReporter { get; }
    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = [];
    public ObservableCollection<ConverterItemViewModel> Converters { get; } = [];
    public ObservableCollection<string> PluginMessages { get; } = [];

    // ---- Folder settings ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string SourceDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string OutputDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeSubdirectories { get; set; } = true;

    [ObservableProperty]
    public partial bool ProcessExistingFiles { get; set; }

    [ObservableProperty]
    public partial int CollisionIndex { get; set; } = 2;

    // ---- Monitoring tuning ----

    [ObservableProperty]
    public partial double DebounceMilliseconds { get; set; } = 500;

    [ObservableProperty]
    public partial double ReconciliationSeconds { get; set; } = 15;

    [ObservableProperty]
    public partial double QueueCapacity { get; set; } = 1000;

    [ObservableProperty]
    public partial int QueueFullIndex { get; set; }

    [ObservableProperty]
    public partial double MaxRetries { get; set; } = 3;

    [ObservableProperty]
    public partial bool DeleteOutputWhenSourceDeleted { get; set; }

    // ---- Application behaviour ----

    [ObservableProperty]
    public partial int JobNotificationIndex { get; set; } = 1;

    [ObservableProperty]
    public partial bool NotifyApplicationEvents { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool StartMonitoringOnLaunch { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial string StartupMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool EnableOcr { get; set; }

    // ---- Integrations ----

    [ObservableProperty]
    public partial string ImageEndpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImageModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImageKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TranscriptionEndpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TranscriptionModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TranscriptionKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DocumentIntelligenceEndpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DocumentIntelligenceKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ContentUnderstandingEndpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ContentUnderstandingKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ContentUnderstandingAnalyzer { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool PluginsEnabled { get; set; }

    [ObservableProperty]
    public partial string TrustedPluginHashes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrustedPluginSigners { get; set; } = string.Empty;

    // ---- State ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(PauseResumeCommand), nameof(RetryFailedCommand), nameof(RetryCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeLabel))]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Ready";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand), nameof(CancelJobCommand), nameof(OpenSourceCommand), nameof(OpenOutputCommand), nameof(ShowOutputInFolderCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelectedJob), nameof(HasNoSelectedJob))]
    public partial JobItemViewModel? SelectedJob { get; set; }

    [ObservableProperty]
    public partial bool ShowWelcome { get; set; } = true;

    [ObservableProperty]
    public partial string DiagnosticsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int CrashReportCount { get; set; }

    [ObservableProperty]
    public partial bool HasValidationIssues { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool HasValidationErrors { get; set; }

    public bool HasSelectedJob => SelectedJob is not null;
    public bool HasNoSelectedJob => SelectedJob is null;
    public string PauseResumeLabel => IsPaused ? "Resume" : "Pause";
    public string ApplicationVersion => AppDiagnostics.ApplicationVersion;

    /// <summary>The settings as last loaded or saved. Exposed for the host window (theme, tray behaviour).</summary>
    public AppSettings Settings => _settings;

    public string SupportedFormatsSummary
    {
        get
        {
            var extensions = _catalog?.Service.SupportedExtensions ?? [];
            return extensions.Count == 0 ? "No converters are enabled." : string.Join("  ", extensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }
    }

    public int FailedJobCount => Jobs.Count(j => j.CanRetry && j.State == ConversionJobState.Failed);

    // ---- Loading and saving ----

    public async Task LoadAsync()
    {
        _loading = true;
        try
        {
            try
            {
                _settings = await _settingsStore.LoadAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _settings = new AppSettings();
                Status = $"Settings could not be loaded and defaults are used: {ex.Message}";
            }

            ApplySettings(_settings);
            RebuildCatalog();

            try
            {
                foreach (var record in await _historyStore.LoadAsync())
                {
                    var model = new JobItemViewModel(record.ToJob());
                    if (_jobModels.TryAdd(model.SourcePath, model))
                    {
                        Jobs.Add(model);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = $"Job history could not be loaded: {ex.Message}";
            }
        }
        finally
        {
            _loading = false;
        }

        Validate();
        RefreshDiagnostics();
    }

    /// <summary>Starts monitoring when the user asked for that at launch and the folders are valid.</summary>
    public async Task StartIfConfiguredAsync()
    {
        if (StartMonitoringOnLaunch && !HasValidationErrors && StartCommand.CanExecute(null))
        {
            await StartCommand.ExecuteAsync(null);
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        SourceDirectory = settings.SourceDirectory;
        OutputDirectory = settings.OutputDirectory;
        IncludeSubdirectories = settings.IncludeSubdirectories;
        ProcessExistingFiles = settings.ProcessExistingFiles;
        CollisionIndex = (int)settings.CollisionBehavior;
        DebounceMilliseconds = settings.DebounceMilliseconds;
        ReconciliationSeconds = settings.ReconciliationSeconds;
        QueueCapacity = settings.QueueCapacity;
        QueueFullIndex = (int)settings.QueueFullBehavior;
        MaxRetries = settings.MaxRetries;
        DeleteOutputWhenSourceDeleted = settings.DeleteOutputWhenSourceDeleted;
        JobNotificationIndex = (int)settings.JobNotifications;
        NotifyApplicationEvents = settings.NotifyApplicationEvents;
        ThemeIndex = (int)settings.Theme;
        StartWithWindows = settings.StartWithWindows;
        StartMonitoringOnLaunch = settings.StartMonitoringOnLaunch;
        MinimizeToTray = settings.MinimizeToTray;
        EnableOcr = settings.EnableOcr;
        ShowWelcome = !settings.HasSeenWelcome;

        var c = settings.Converters;
        ImageEndpoint = c.ImageDescription.Endpoint;
        ImageModel = c.ImageDescription.Model;
        ImageKey = c.ImageDescription.ApiKey ?? string.Empty;
        TranscriptionEndpoint = c.Transcription.Endpoint;
        TranscriptionModel = c.Transcription.Model;
        TranscriptionKey = c.Transcription.ApiKey ?? string.Empty;
        DocumentIntelligenceEndpoint = c.DocumentIntelligence.Endpoint;
        DocumentIntelligenceKey = c.DocumentIntelligence.ApiKey ?? string.Empty;
        ContentUnderstandingEndpoint = c.ContentUnderstanding.Endpoint;
        ContentUnderstandingKey = c.ContentUnderstanding.ApiKey ?? string.Empty;
        ContentUnderstandingAnalyzer = c.ContentUnderstanding.AnalyzerId ?? string.Empty;
        PluginsEnabled = c.PluginPolicy.Enabled;
        TrustedPluginHashes = string.Join(Environment.NewLine, c.PluginPolicy.TrustedSha256);
        TrustedPluginSigners = string.Join(Environment.NewLine, c.PluginPolicy.TrustedSignerThumbprints);
    }

    private AppSettings CaptureSettings()
    {
        var converters = _settings.Converters;
        converters.ImageDescription = new OpenAiCompatibleSettings { Endpoint = ImageEndpoint.Trim(), Model = ImageModel.Trim(), ApiKey = NullIfEmpty(ImageKey) };
        converters.Transcription = new OpenAiCompatibleSettings { Endpoint = TranscriptionEndpoint.Trim(), Model = TranscriptionModel.Trim(), ApiKey = NullIfEmpty(TranscriptionKey) };
        converters.DocumentIntelligence = new AzureSettings { Endpoint = DocumentIntelligenceEndpoint.Trim(), ApiKey = NullIfEmpty(DocumentIntelligenceKey) };
        converters.ContentUnderstanding = new AzureSettings
        {
            Endpoint = ContentUnderstandingEndpoint.Trim(),
            ApiKey = NullIfEmpty(ContentUnderstandingKey),
            AnalyzerId = NullIfEmpty(ContentUnderstandingAnalyzer)
        };
        converters.PluginDirectory = Paths.PluginDirectory;
        converters.PluginPolicy = new PluginTrustPolicy
        {
            Enabled = PluginsEnabled,
            TrustedSha256 = SplitLines(TrustedPluginHashes),
            TrustedSignerThumbprints = SplitLines(TrustedPluginSigners)
        };

        _settings.SourceDirectory = SourceDirectory;
        _settings.OutputDirectory = OutputDirectory;
        _settings.IncludeSubdirectories = IncludeSubdirectories;
        _settings.ProcessExistingFiles = ProcessExistingFiles;
        _settings.CollisionBehavior = (CollisionBehavior)CollisionIndex;
        _settings.DebounceMilliseconds = (int)Math.Clamp(DebounceMilliseconds, 0, 60_000);
        _settings.ReconciliationSeconds = (int)Math.Clamp(ReconciliationSeconds, 1, 86_400);
        _settings.QueueCapacity = (int)Math.Clamp(QueueCapacity, 1, 100_000);
        _settings.QueueFullBehavior = (QueueFullBehavior)QueueFullIndex;
        _settings.MaxRetries = (int)Math.Clamp(MaxRetries, 0, 20);
        _settings.DeleteOutputWhenSourceDeleted = DeleteOutputWhenSourceDeleted;
        _settings.JobNotifications = (JobNotificationMode)JobNotificationIndex;
        _settings.NotifyApplicationEvents = NotifyApplicationEvents;
        _settings.Theme = (AppTheme)ThemeIndex;
        _settings.StartWithWindows = StartWithWindows;
        _settings.StartMonitoringOnLaunch = StartMonitoringOnLaunch;
        _settings.MinimizeToTray = MinimizeToTray;
        _settings.EnableOcr = EnableOcr;
        _settings.HasSeenWelcome = !ShowWelcome;
        return _settings;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> SplitLines(string text) =>
        text.Split(['\r', '\n', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(CaptureSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Settings could not be saved: {ex.Message}";
        }
    }

    // ---- Property change reactions ----

    partial void OnSourceDirectoryChanged(string value) => Validate();

    partial void OnOutputDirectoryChanged(string value) => Validate();

    partial void OnIsPausedChanged(bool value) => PauseResumeCommand.NotifyCanExecuteChanged();

    partial void OnShowWelcomeChanged(bool value)
    {
        if (!_loading && !value)
        {
            _ = SaveSettingsAsync();
        }
    }

    /// <summary>Re-checks the folder settings. Called when a folder changes and again just before monitoring starts.</summary>
    public void Validate()
    {
        var issues = MonitorSettingsValidator.Validate(SourceDirectory, OutputDirectory);
        ValidationIssues.Clear();
        foreach (var issue in issues)
        {
            ValidationIssues.Add(issue);
        }

        HasValidationIssues = issues.Count > 0;
        HasValidationErrors = MonitorSettingsValidator.HasErrors(issues);
    }

    // ---- Monitoring ----

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        Validate();
        if (HasValidationErrors)
        {
            Status = ValidationIssues.First(i => i.Severity == ValidationSeverity.Error).Message;
            return;
        }

        Status = "Starting monitor...";
        try
        {
            ShowWelcome = false;
            await SaveSettingsAsync();
            RebuildCatalog();
            var service = _catalog!.Service;

            _monitor = new FolderMonitorService(service);
            _monitor.JobCreated += OnJobCreated;
            _monitor.JobChanged += OnJobChanged;
            _monitor.MonitorError += OnMonitorError;
            _monitor.PauseChanged += OnPauseChanged;
            _monitor.SourceDeleted += OnSourceDeleted;

            await _monitor.StartAsync(new MonitorOptions(
                SourceDirectory,
                OutputDirectory,
                IncludeSubdirectories,
                ProcessExistingFiles,
                (CollisionBehavior)CollisionIndex)
            {
                Debounce = TimeSpan.FromMilliseconds(_settings.DebounceMilliseconds),
                ReconciliationInterval = TimeSpan.FromSeconds(_settings.ReconciliationSeconds),
                QueueCapacity = _settings.QueueCapacity,
                QueueFullBehavior = _settings.QueueFullBehavior,
                MaxRetries = _settings.MaxRetries,
                DeleteOutputWhenSourceDeleted = _settings.DeleteOutputWhenSourceDeleted
            });

            IsRunning = true;
            IsPaused = false;
            Status = $"Monitoring {SourceDirectory}";
            NotifyApplication("Monitoring started", SourceDirectory);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            if (_monitor is not null)
            {
                await _monitor.DisposeAsync();
                _monitor = null;
            }
        }
    }

    private bool CanStart() => !IsRunning && !HasValidationErrors;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        await StopMonitorAsync(flush: true);
        Status = "Stopped";
        NotifyApplication("Monitoring stopped", "Queued files were finished first.");
    }

    private async Task StopMonitorAsync(bool flush)
    {
        var monitor = _monitor;
        _monitor = null;
        if (monitor is not null)
        {
            Status = flush ? "Finishing queued files..." : "Stopping...";
            await monitor.StopAsync(flush, TimeSpan.FromSeconds(10));
            await monitor.DisposeAsync();
        }

        IsRunning = false;
        IsPaused = false;
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void PauseResume()
    {
        if (_monitor is null)
        {
            return;
        }

        if (_monitor.IsPaused)
        {
            _monitor.Resume();
            Status = $"Monitoring {SourceDirectory}";
        }
        else
        {
            _monitor.Pause();
            Status = "Paused. Detected files wait in the queue until you resume.";
        }

        // Already on the UI thread here, so show the new state immediately.
        IsPaused = _monitor.IsPaused;
    }

    private void OnPauseChanged(object? sender, EventArgs e) =>
        _dispatcher.Post(() => IsPaused = _monitor?.IsPaused ?? false);

    // ---- Jobs ----

    // Events arrive on worker threads and the job object keeps changing, so the state is captured here, at the
    // moment of the event, and applied later on the UI thread. Otherwise a busy UI would see only the final state.
    private void OnJobCreated(object? sender, ConversionJob job)
    {
        var snapshot = JobSnapshot.From(job);
        _dispatcher.Post(() => GetOrAddModel(job, snapshot, rebind: true));
    }

    private void OnJobChanged(object? sender, ConversionJob job)
    {
        var snapshot = JobSnapshot.From(job);
        _dispatcher.Post(() =>
        {
            var model = GetOrAddModel(job, snapshot, rebind: false);
            model.Update(snapshot);
            OnPropertyChanged(nameof(FailedJobCount));
            RetryFailedCommand.NotifyCanExecuteChanged();
            RetryCommand.NotifyCanExecuteChanged();
            CancelJobCommand.NotifyCanExecuteChanged();

            if (snapshot.State is ConversionJobState.Completed or ConversionJobState.Failed or ConversionJobState.Skipped or ConversionJobState.Cancelled)
            {
                Status = $"{model.FileName}: {model.StateText}";
                _ = SaveHistoryAsync();
                NotifyJob(model);
            }
        });
    }

    private JobItemViewModel GetOrAddModel(ConversionJob job, JobSnapshot snapshot, bool rebind)
    {
        if (_jobModels.TryGetValue(job.SourcePath, out var existing))
        {
            if (rebind)
            {
                existing.Rebind(job, snapshot);
                Jobs.Remove(existing);
                Jobs.Insert(0, existing);
            }

            return existing;
        }

        var model = new JobItemViewModel(job);
        model.Update(snapshot);
        _jobModels[job.SourcePath] = model;
        Jobs.Insert(0, model);
        return model;
    }

    private void OnMonitorError(object? sender, Exception error) =>
        _dispatcher.Post(() =>
        {
            Status = $"Monitor error: {error.Message}";
            NotifyApplication("Monitoring problem", error.Message);
        });

    private void OnSourceDeleted(object? sender, SourceDeletedEventArgs e) =>
        _dispatcher.Post(() => Status = $"Source removed: {Path.GetFileName(e.SourcePath)}" + (e.OutputDeleted ? " (output deleted)" : string.Empty));

    [RelayCommand(CanExecute = nameof(CanRetrySelected))]
    private async Task RetryAsync(JobItemViewModel? item)
    {
        item ??= SelectedJob;
        if (item is not null && _monitor is not null && !await _monitor.RetryAsync(item.Job))
        {
            Status = $"'{item.FileName}' cannot be retried: the source file may no longer exist.";
        }
    }

    private bool CanRetrySelected(JobItemViewModel? item) => IsRunning && (item ?? SelectedJob)?.CanRetry == true;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task RetryFailedAsync()
    {
        if (_monitor is null)
        {
            return;
        }

        foreach (var item in Jobs.Where(j => j.State == ConversionJobState.Failed).ToList())
        {
            await _monitor.RetryAsync(item.Job);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelSelected))]
    private void CancelJob(JobItemViewModel? item)
    {
        item ??= SelectedJob;
        item?.Job.Cancel();
    }

    private bool CanCancelSelected(JobItemViewModel? item) => (item ?? SelectedJob)?.CanCancel == true;

    [RelayCommand(CanExecute = nameof(CanOpenSource))]
    private void OpenSource(JobItemViewModel? item) => OpenSafely((item ?? SelectedJob)?.SourcePath, reveal: false);

    private bool CanOpenSource(JobItemViewModel? item) => (item ?? SelectedJob) is { } job && File.Exists(job.SourcePath);

    [RelayCommand(CanExecute = nameof(CanOpenOutput))]
    private void OpenOutput(JobItemViewModel? item) => OpenSafely((item ?? SelectedJob)?.OutputPath, reveal: false);

    [RelayCommand(CanExecute = nameof(CanOpenOutput))]
    private void ShowOutputInFolder(JobItemViewModel? item) => OpenSafely((item ?? SelectedJob)?.OutputPath, reveal: true);

    private bool CanOpenOutput(JobItemViewModel? item) =>
        (item ?? SelectedJob) is { State: ConversionJobState.Completed } job && File.Exists(job.OutputPath);

    private void OpenSafely(string? path, bool reveal)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (reveal)
            {
                _shell.ShowInFolder(path);
            }
            else
            {
                _shell.OpenFile(path);
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = $"Could not open '{Path.GetFileName(path)}': {ex.Message}";
        }
    }

    /// <summary>Removes finished jobs from the list and from the saved history.</summary>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        foreach (var item in Jobs.Where(j => j.IsFinished).ToList())
        {
            Jobs.Remove(item);
            _jobModels.Remove(item.SourcePath);
        }

        SelectedJob = null;
        OnPropertyChanged(nameof(FailedJobCount));
        await SaveHistoryAsync();
        Status = "History cleared.";
    }

    private Task SaveHistoryAsync()
    {
        // Snapshot now (on the UI thread); write in order so an older snapshot can never overwrite a newer one.
        var records = Jobs.Where(j => j.IsFinished).Select(j => j.ToRecord()).ToList();
        lock (_historySaveLock)
        {
            _historySaveChain = WriteHistoryAsync(_historySaveChain, records);
            return _historySaveChain;
        }
    }

    private async Task WriteHistoryAsync(Task previous, List<JobRecord> records)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await _historyStore.SaveAsync(records).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dispatcher.Post(() => Status = $"Job history could not be saved: {ex.Message}");
        }
    }

    // ---- Notifications ----

    private void NotifyJob(JobItemViewModel job)
    {
        var mode = (JobNotificationMode)JobNotificationIndex;
        var wanted = mode switch
        {
            JobNotificationMode.All => job.State is ConversionJobState.Completed or ConversionJobState.Failed,
            JobNotificationMode.FailuresOnly => job.State == ConversionJobState.Failed,
            _ => false
        };

        if (!wanted)
        {
            return;
        }

        var title = job.State == ConversionJobState.Completed ? "Converted" : "Conversion failed";
        var message = job.State == ConversionJobState.Completed ? job.FileName : $"{job.FileName}: {job.Error}";
        Toast(title, message);
    }

    private void NotifyApplication(string title, string message)
    {
        if (NotifyApplicationEvents)
        {
            Toast(title, message);
        }
    }

    // Large batches would flood the notification centre, so only a few toasts go out per window.
    private void Toast(string title, string message)
    {
        var now = DateTimeOffset.Now;
        while (_recentToasts.Count > 0 && now - _recentToasts.Peek() > ToastWindow)
        {
            _recentToasts.Dequeue();
            _toastSummarySent = false;
        }

        if (_recentToasts.Count >= MaxToastsPerWindow)
        {
            if (!_toastSummarySent)
            {
                _toastSummarySent = true;
                SafeNotify("MarkItDownSharp", "More files are being processed. Open the app to see all results.");
            }

            return;
        }

        _recentToasts.Enqueue(now);
        SafeNotify(title, message);
    }

    private void SafeNotify(string title, string message)
    {
        try
        {
            _shell.Notify(title, message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            // Notifications can be switched off in Windows; that must not disturb conversion.
            Status = $"Notification could not be shown: {ex.Message}";
        }
    }

    // ---- Converters and integrations ----

    private void RebuildCatalog()
    {
        var converterSettings = _settings.Converters;
        converterSettings.PluginDirectory = Paths.PluginDirectory;
        _catalog = ConverterCatalog.Create(converterSettings, _settings.EnableOcr ? _ocr : null);
        _catalog.Service.ConvertersChanged += (_, _) => OnPropertyChanged(nameof(SupportedFormatsSummary));

        Converters.Clear();
        foreach (var info in _catalog.Service.Describe())
        {
            Converters.Add(new ConverterItemViewModel(info, SetConverterEnabled));
        }

        PluginMessages.Clear();
        foreach (var plugin in _catalog.PluginResults)
        {
            PluginMessages.Add($"{Path.GetFileName(plugin.Path)}: {(plugin.Loaded ? "loaded" : "refused")} - {plugin.Reason}");
        }

        OnPropertyChanged(nameof(SupportedFormatsSummary));
        RefreshDiagnostics();
    }

    private void SetConverterEnabled(ConverterItemViewModel item, bool enabled)
    {
        if (_catalog is null)
        {
            return;
        }

        _catalog.Service.SetEnabled(item.Name, enabled);
        _settings.Converters.DisabledConverters = _catalog.Service.DisabledConverters.ToList();
        _ = SaveSettingsAsync();
        RefreshDiagnostics();
        if (IsRunning)
        {
            Status = "Converter changes apply the next time monitoring starts.";
        }
    }

    /// <summary>Saves integration, plugin and application settings and rebuilds the converter set.</summary>
    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        await SaveSettingsAsync();
        RebuildCatalog();
        Status = IsRunning
            ? "Settings saved. Integration and plugin changes apply the next time monitoring starts."
            : "Settings saved.";
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_loading)
        {
            _ = ApplyStartupAsync(value);
        }
    }

    private async Task ApplyStartupAsync(bool enabled)
    {
        var result = await _shell.SetStartWithWindowsAsync(enabled);
        _dispatcher.Post(() =>
        {
            StartupMessage = result.Message;
            if (!result.Succeeded && StartWithWindows == enabled)
            {
                _loading = true;
                StartWithWindows = !enabled;
                _loading = false;
            }
        });
        await SaveSettingsAsync();
    }

    public void RefreshDiagnostics()
    {
        DiagnosticsText = AppDiagnostics.Describe(_catalog?.Service, _catalog?.PluginResults);
        CrashReportCount = CrashReporter.ReportCount;
    }

    [RelayCommand]
    private void DismissWelcome() => ShowWelcome = false;

    public async ValueTask DisposeAsync()
    {
        await StopMonitorAsync(flush: true);
        await SaveSettingsAsync();

        // Make sure every queued history write has reached the disk before the process can exit.
        Task pending;
        lock (_historySaveLock)
        {
            pending = _historySaveChain;
        }

        await pending.ConfigureAwait(false);
    }
}

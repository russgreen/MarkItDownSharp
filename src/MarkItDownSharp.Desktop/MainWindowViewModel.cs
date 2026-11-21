using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkItDownSharp.Converters;
using MarkItDownSharp.Core;
using MarkItDownSharp.Worker;
using Microsoft.UI.Dispatching;

namespace MarkItDownSharp.Desktop;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherQueue _dispatcher;
    private FolderMonitorService? _monitor;
    private readonly Dictionary<string, JobItemViewModel> _jobModels = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Ready";

    public async Task LoadAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            SourceDirectory = settings.SourceDirectory;
            OutputDirectory = settings.OutputDirectory;
            IncludeSubdirectories = settings.IncludeSubdirectories;
            ProcessExistingFiles = settings.ProcessExistingFiles;
            CollisionIndex = (int)settings.CollisionBehavior;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Status = $"Settings could not be loaded: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        Status = "Starting monitor...";
        try
        {
            var converter = new MarkItDownService(new IMarkItDownConverter[]
            {
                new TextConverter(),
                new HtmlConverter(),
                new CsvConverter(),
                new StructuredTextConverter()
            });
            _monitor = new FolderMonitorService(converter);
            _monitor.JobCreated += OnJobCreated;
            _monitor.JobChanged += OnJobChanged;
            _monitor.MonitorError += OnMonitorError;

            await _monitor.StartAsync(new MonitorOptions(
                SourceDirectory,
                OutputDirectory,
                IncludeSubdirectories,
                ProcessExistingFiles,
                (CollisionBehavior)CollisionIndex));

            await SaveSettingsAsync();
            IsRunning = true;
            Status = $"Monitoring {SourceDirectory}";
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

    private bool CanStart() =>
        !IsRunning &&
        !string.IsNullOrWhiteSpace(SourceDirectory) &&
        !string.IsNullOrWhiteSpace(OutputDirectory);

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        if (_monitor is not null)
        {
            await _monitor.DisposeAsync();
            _monitor = null;
        }

        IsRunning = false;
        Status = "Stopped";
    }

    private void OnJobCreated(object? sender, ConversionJob job)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_jobModels.TryAdd(job.SourcePath, new JobItemViewModel(job)))
            {
                Jobs.Insert(0, _jobModels[job.SourcePath]);
            }
        });
    }

    private void OnJobChanged(object? sender, ConversionJob job)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (!_jobModels.TryGetValue(job.SourcePath, out var model))
            {
                model = new JobItemViewModel(job);
                _jobModels[job.SourcePath] = model;
                Jobs.Insert(0, model);
            }

            model.Update(job);
            Status = $"{job.FileName}: {job.State}";
        });
    }

    private void OnMonitorError(object? sender, Exception error) =>
        _dispatcher.TryEnqueue(() => Status = $"Monitor error: {error.Message}");

    private Task SaveSettingsAsync() =>
        _settingsStore.SaveAsync(new AppSettings
        {
            SourceDirectory = SourceDirectory,
            OutputDirectory = OutputDirectory,
            IncludeSubdirectories = IncludeSubdirectories,
            ProcessExistingFiles = ProcessExistingFiles,
            CollisionBehavior = (CollisionBehavior)CollisionIndex
        });

    public async ValueTask DisposeAsync()
    {
        if (_monitor is not null)
        {
            await _monitor.DisposeAsync();
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using MarkItDownSharp.Desktop.Services;
using MarkItDownSharp.Presentation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MarkItDownSharp.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly nint _handle;
    private TrayIcon? _tray;
    private bool _exiting;
    private bool _shutdownComplete;

    public MainWindow(bool launchedFromStartup = false)
    {
        InitializeComponent();
        _handle = WindowNative.GetWindowHandle(this);
        ViewModel = new MainViewModel(
            new DispatcherQueueAdapter(DispatcherQueue),
            new WindowsShellServices(),
            AppPaths.Default,
            ocr: new WindowsOcrEngine());
        ViewModel.CrashReporter.Install();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
        AppWindow.Closing += AppWindow_Closing;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _ = InitializeAsync(launchedFromStartup);
    }

    public MainViewModel ViewModel { get; }

    private async Task InitializeAsync(bool launchedFromStartup)
    {
        await ViewModel.LoadAsync();
        ApplyTheme();
        UpdateTray();
        await ViewModel.StartIfConfiguredAsync();

        if (launchedFromStartup)
        {
            // Started by Windows at sign-in: stay out of the way.
            if (ViewModel.MinimizeToTray)
            {
                AppWindow.Hide();
            }
            else if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ThemeIndex):
                ApplyTheme();
                break;
            case nameof(MainViewModel.MinimizeToTray):
                UpdateTray();
                break;
            case nameof(MainViewModel.Status):
            case nameof(MainViewModel.IsPaused):
                _tray?.SetTooltip(TrayTooltip());
                break;
        }
    }

    // Colours come from theme resources, so dark, light and high-contrast all follow the chosen theme.
    private void ApplyTheme() =>
        RootGrid.RequestedTheme = ViewModel.ThemeIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

    private string TrayTooltip() =>
        "MarkItDownSharp" + (ViewModel.IsRunning ? (ViewModel.IsPaused ? " (paused)" : " (monitoring)") : string.Empty);

    private void UpdateTray()
    {
        if (ViewModel.MinimizeToTray)
        {
            _tray ??= new TrayIcon(
                _handle,
                Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
                TrayTooltip(),
                open: ShowWindow,
                togglePause: () => DispatcherQueue.TryEnqueue(() => ViewModel.PauseResumeCommand.Execute(null)),
                isPaused: () => ViewModel.IsPaused,
                exit: () => DispatcherQueue.TryEnqueue(ExitApplication));
            _tray.Show();
        }
        else
        {
            _tray?.Dispose();
            _tray = null;
        }
    }

    private void ShowWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AppWindow.Show();
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            {
                presenter.Restore();
            }

            Activate();
        });
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    // Closing the window hides it when the app runs from the notification area; otherwise queued files are
    // finished before the window really closes.
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownComplete)
        {
            return;
        }

        if (ViewModel.MinimizeToTray && !_exiting)
        {
            args.Cancel = true;
            sender.Hide();
            return;
        }

        args.Cancel = true;
        try
        {
            await ViewModel.DisposeAsync();
        }
        finally
        {
            _tray?.Dispose();
            _shutdownComplete = true;
            Close();
        }
    }

    private async void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            ViewModel.SourceDirectory = path;
        }
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            ViewModel.OutputDirectory = path;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _handle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void Sections_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.Items.IndexOf(sender.SelectedItem);
        ConversionsPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ConvertersPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (index == 3)
        {
            ViewModel.RefreshDiagnostics();
        }
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.DiagnosticsText);
        Clipboard.SetContent(package);
        ViewModel.Status = "Diagnostics copied to the clipboard.";
    }

    private void OpenCrashFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(ViewModel.CrashReporter.ReportDirectory);

    private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(ViewModel.Paths.PluginDirectory);

    private void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
    }
}

using MarkItDownSharp.Desktop.Services;
using MarkItDownSharp.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace MarkItDownSharp.Desktop;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // Crash reports hold the exception type and call stack only; see CrashReporter.
        var reporter = new CrashReporter(AppPaths.Default);
        reporter.Install();
        UnhandledException += (_, e) => reporter.Write(e.Exception, "Application.UnhandledException");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // MSIX starts raise a StartupTask activation; the MSI build is started with --startup by its Run key.
        var launchedFromStartup = activation.Kind == ExtendedActivationKind.StartupTask ||
            Environment.GetCommandLineArgs().Contains(WindowsShellServices.StartupArgument, StringComparer.OrdinalIgnoreCase);
        _window = new MainWindow(launchedFromStartup);

        // A startup launch decides in the window whether to show it, minimise it or hide it in the tray.
        if (!launchedFromStartup)
        {
            _window.Activate();
        }
    }
}

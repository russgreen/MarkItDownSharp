using System.Diagnostics;
using System.Runtime.InteropServices;
using MarkItDownSharp.Presentation;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.ApplicationModel;

namespace MarkItDownSharp.Desktop.Services;

/// <summary>Windows implementation of the shell actions the view model asks for.</summary>
public sealed class WindowsShellServices : IShellServices
{
    public const string StartupTaskId = "MarkItDownSharpStartup";

    private bool _notificationsRegistered;

    public void OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    public void ShowInFolder(string path)
    {
        // The path is passed as one quoted argument so file names containing spaces or commas are not split.
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
    }

    public void Notify(string title, string message)
    {
        EnsureNotificationsRegistered();
        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .BuildNotification();
        AppNotificationManager.Default.Show(notification);
    }

    public async Task<StartupResult> SetStartWithWindowsAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                var state = await task.RequestEnableAsync();
                return state switch
                {
                    StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => new StartupResult(true, "MarkItDownSharp starts when you sign in to Windows."),
                    StartupTaskState.DisabledByUser => new StartupResult(false, "Startup is turned off for this app in Windows Settings > Apps > Startup. Turn it on there, then try again."),
                    StartupTaskState.DisabledByPolicy => new StartupResult(false, "Startup apps are disabled by your organisation's policy."),
                    _ => new StartupResult(false, $"Windows did not enable startup ({state}).")
                };
            }

            task.Disable();
            return new StartupResult(true, "MarkItDownSharp will not start automatically.");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or FileNotFoundException)
        {
            return new StartupResult(false, "Starting with Windows needs the installed (MSIX) version of the app.");
        }
    }

    private void EnsureNotificationsRegistered()
    {
        if (_notificationsRegistered)
        {
            return;
        }

        AppNotificationManager.Default.Register();
        _notificationsRegistered = true;
    }
}

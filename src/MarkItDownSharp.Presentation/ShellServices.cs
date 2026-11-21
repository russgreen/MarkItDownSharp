namespace MarkItDownSharp.Presentation;

/// <summary>Runs work on the thread that owns the user interface.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public readonly record struct StartupResult(bool Succeeded, string Message);

/// <summary>Operating-system actions the view model needs but cannot perform itself.</summary>
public interface IShellServices
{
    /// <summary>Opens a file with its default application.</summary>
    void OpenFile(string path);

    /// <summary>Shows a file selected in File Explorer.</summary>
    void ShowInFolder(string path);

    void Notify(string title, string message);

    /// <summary>Registers or removes the application as a start-with-Windows task.</summary>
    Task<StartupResult> SetStartWithWindowsAsync(bool enabled);
}

namespace MarkItDownSharp.Presentation;

/// <summary>Where the application keeps its files. Tests point this at a temporary folder.</summary>
public sealed class AppPaths
{
    public AppPaths(string dataDirectory)
    {
        DataDirectory = dataDirectory;
    }

    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarkItDownSharp"));

    public string DataDirectory { get; }
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string HistoryFile => Path.Combine(DataDirectory, "history.json");
    public string CrashReportDirectory => Path.Combine(DataDirectory, "crash-reports");
    public string PluginDirectory => Path.Combine(DataDirectory, "plugins");
}

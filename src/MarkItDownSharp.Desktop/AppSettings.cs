using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Desktop;

public sealed class AppSettings
{
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public bool IncludeSubdirectories { get; set; } = true;
    public bool ProcessExistingFiles { get; set; }
    public CollisionBehavior CollisionBehavior { get; set; } = CollisionBehavior.Version;
}

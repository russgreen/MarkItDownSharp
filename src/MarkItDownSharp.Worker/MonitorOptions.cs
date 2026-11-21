namespace MarkItDownSharp.Worker;

public sealed record MonitorOptions(
    string InputDirectory,
    string OutputDirectory,
    bool IncludeSubdirectories = true,
    bool ProcessExistingFiles = false,
    CollisionBehavior CollisionBehavior = CollisionBehavior.Version);

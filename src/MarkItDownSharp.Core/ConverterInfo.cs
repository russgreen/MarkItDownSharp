namespace MarkItDownSharp.Core;

/// <summary>
/// A read-only description of a registered converter, used by the diagnostics page and settings.
/// </summary>
public sealed record ConverterInfo(
    string Name,
    string Description,
    IReadOnlyCollection<string> Extensions,
    int Priority,
    bool RequiresNetwork,
    bool IsEnabled,
    string AssemblyName,
    string Version,
    string Origin);

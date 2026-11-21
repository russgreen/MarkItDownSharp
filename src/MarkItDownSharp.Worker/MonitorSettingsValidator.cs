namespace MarkItDownSharp.Worker;

public enum ValidationSeverity
{
    Warning,
    Error
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Field, string Message);

/// <summary>
/// Checks folder settings before monitoring starts and explains what is wrong in plain language.
/// </summary>
public static class MonitorSettingsValidator
{
    public const string InputField = "Input";
    public const string OutputField = "Output";

    public static IReadOnlyList<ValidationIssue> Validate(string? inputDirectory, string? outputDirectory)
    {
        var issues = new List<ValidationIssue>();
        var input = ValidateInput(inputDirectory, issues);
        var output = ValidateOutput(outputDirectory, issues);

        if (input is not null && output is not null)
        {
            var inputKey = Path.TrimEndingDirectorySeparator(input);
            var outputKey = Path.TrimEndingDirectorySeparator(output);
            if (string.Equals(inputKey, outputKey, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new(ValidationSeverity.Error, OutputField, "The output folder must be different from the monitor folder, otherwise converted files would be converted again."));
            }
            else if (IsInside(output, input))
            {
                issues.Add(new(ValidationSeverity.Warning, OutputField, "The output folder is inside the monitor folder. Its contents will be ignored while monitoring."));
            }
            else if (IsInside(input, output))
            {
                issues.Add(new(ValidationSeverity.Warning, InputField, "The monitor folder is inside the output folder. Converted files will be written next to it."));
            }
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<ValidationIssue> issues) =>
        issues.Any(i => i.Severity == ValidationSeverity.Error);

    private static string? ValidateInput(string? value, List<ValidationIssue> issues)
    {
        if (!TryNormalize(value, InputField, "monitor", issues, out var path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            issues.Add(new(ValidationSeverity.Error, InputField, $"'{path}' is a file. Choose a folder to monitor."));
            return null;
        }

        if (!Directory.Exists(path))
        {
            issues.Add(new(ValidationSeverity.Error, InputField, $"The monitor folder '{path}' does not exist or is not reachable."));
            return null;
        }

        try
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            enumerator.MoveNext();
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(new(ValidationSeverity.Error, InputField, $"You do not have permission to read the monitor folder '{path}'."));
            return null;
        }
        catch (IOException ex)
        {
            issues.Add(new(ValidationSeverity.Error, InputField, $"The monitor folder '{path}' cannot be read: {ex.Message}"));
            return null;
        }

        if (IsNetworkPath(path))
        {
            issues.Add(new(ValidationSeverity.Warning, InputField, "Network folders can miss change notifications. New files are still found by the periodic rescan, but they may appear with a delay."));
        }

        return path;
    }

    private static string? ValidateOutput(string? value, List<ValidationIssue> issues)
    {
        if (!TryNormalize(value, OutputField, "output", issues, out var path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            issues.Add(new(ValidationSeverity.Error, OutputField, $"'{path}' is a file. Choose a folder for the converted Markdown."));
            return null;
        }

        // Probe the folder, or the closest existing parent when it will be created on start.
        var probeFolder = path;
        while (!Directory.Exists(probeFolder))
        {
            probeFolder = Path.GetDirectoryName(probeFolder);
            if (probeFolder is null)
            {
                issues.Add(new(ValidationSeverity.Error, OutputField, $"The drive or share for '{path}' does not exist or is not reachable."));
                return null;
            }
        }

        try
        {
            var probe = Path.Combine(probeFolder, $".markitdownsharp-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(new(ValidationSeverity.Error, OutputField, $"You do not have permission to write to '{probeFolder}'. Choose a folder you can write to."));
            return null;
        }
        catch (IOException ex)
        {
            issues.Add(new(ValidationSeverity.Error, OutputField, $"The output location '{probeFolder}' is not writable: {ex.Message}"));
            return null;
        }

        return path;
    }

    private static bool TryNormalize(string? value, string field, string label, List<ValidationIssue> issues, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new(ValidationSeverity.Error, field, $"Choose a {label} folder."));
            return false;
        }

        try
        {
            path = Path.GetFullPath(value.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            issues.Add(new(ValidationSeverity.Error, field, $"'{value}' is not a valid folder path: {ex.Message}"));
            return false;
        }

        return true;
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }
}

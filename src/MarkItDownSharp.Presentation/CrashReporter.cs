using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkItDownSharp.Presentation;

/// <summary>
/// Writes crash reports to a local folder. A report contains versions and the exception call stack only:
/// no exception messages (they often quote file names or content), no file paths, no document content.
/// Nothing is uploaded; the user can attach a report to a bug report manually.
/// </summary>
public sealed partial class CrashReporter
{
    private const int MaxReports = 20;

    private readonly AppPaths _paths;

    public CrashReporter(AppPaths? paths = null)
    {
        _paths = paths ?? AppPaths.Default;
    }

    public string ReportDirectory => _paths.CrashReportDirectory;

    public int ReportCount =>
        Directory.Exists(ReportDirectory) ? Directory.EnumerateFiles(ReportDirectory, "crash-*.txt").Count() : 0;

    /// <summary>Hooks process-wide unhandled exception events.</summary>
    public void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Write(exception, "AppDomain.UnhandledException");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Write(e.Exception, "TaskScheduler.UnobservedTaskException");
    }

    /// <returns>The report path, or <see langword="null"/> when it could not be written.</returns>
    public string? Write(Exception exception, string source)
    {
        try
        {
            Directory.CreateDirectory(ReportDirectory);
            var path = Path.Combine(ReportDirectory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(path, Format(exception, source));
            Trim();
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reporting a crash must never cause another one.
            return null;
        }
    }

    public static string Format(Exception exception, string source)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MarkItDownSharp crash report");
        builder.AppendLine("No document names, paths or content are included.");
        builder.AppendLine();
        builder.AppendLine($"Time (UTC): {DateTime.UtcNow:O}");
        builder.AppendLine($"Version: {AppDiagnostics.ApplicationVersion}");
        builder.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine();

        var depth = 0;
        for (var current = exception; current is not null && depth < 5; current = current.InnerException, depth++)
        {
            builder.AppendLine(depth == 0 ? $"Exception: {current.GetType().FullName}" : $"Caused by: {current.GetType().FullName}");
            builder.AppendLine("  HResult: 0x" + current.HResult.ToString("X8"));
            foreach (var line in (current.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                builder.AppendLine("  " + StripLocation(line.Trim()));
            }
        }

        return builder.ToString();
    }

    // "at Namespace.Type.Method(Args) in C:\src\File.cs:line 12" -> "at Namespace.Type.Method(Args)"
    private static string StripLocation(string frame) => LocationSuffix().Replace(frame, string.Empty);

    [GeneratedRegex(@"\s+in\s+.*$")]
    private static partial Regex LocationSuffix();

    private void Trim()
    {
        var old = Directory.EnumerateFiles(ReportDirectory, "crash-*.txt")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(MaxReports);
        foreach (var file in old)
        {
            File.Delete(file);
        }
    }
}

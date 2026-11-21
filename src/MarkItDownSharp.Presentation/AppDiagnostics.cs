using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MarkItDownSharp.Core;
using MarkItDownSharp.Core.Plugins;

namespace MarkItDownSharp.Presentation;

/// <summary>
/// Builds the text shown on the diagnostics page and copied into bug reports. It contains versions only,
/// never document names, paths or content.
/// </summary>
public static class AppDiagnostics
{
    public static string ApplicationVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AppDiagnostics).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            // Drop the source-control suffix ("1.0.0+abc123") from the display version.
            return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }

    public static string Describe(MarkItDownService? service, IReadOnlyList<PluginLoadResult>? pluginResults = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"MarkItDownSharp {ApplicationVersion}");
        builder.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine();
        builder.AppendLine("Converters:");
        foreach (var converter in service?.Describe() ?? [])
        {
            builder.Append("  ")
                .Append(converter.IsEnabled ? "[on]  " : "[off] ")
                .Append(converter.Name)
                .Append(" (").Append(converter.Origin).Append(", ").Append(converter.AssemblyName).Append(' ').Append(converter.Version)
                .Append(", priority ").Append(converter.Priority).AppendLine(")");
        }

        if (pluginResults is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("Plugins:");
            foreach (var plugin in pluginResults)
            {
                builder.AppendLine($"  {Path.GetFileName(plugin.Path)}: {(plugin.Loaded ? "loaded" : "refused")} - {plugin.Reason}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}

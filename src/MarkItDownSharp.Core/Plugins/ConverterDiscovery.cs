using System.Reflection;
using System.Runtime.Loader;

namespace MarkItDownSharp.Core.Plugins;

public sealed record PluginLoadResult(string Path, bool Loaded, string Reason, string? Sha256, IReadOnlyList<string> Converters);

public sealed class PluginLoadReport
{
    public IReadOnlyList<IMarkItDownConverter> Converters { get; init; } = [];
    public IReadOnlyList<PluginLoadResult> Results { get; init; } = [];
}

/// <summary>
/// Finds <see cref="IMarkItDownConverter"/> implementations in assemblies and plugin folders.
/// </summary>
public static class ConverterDiscovery
{
    /// <summary>
    /// Creates every public, non-abstract converter in the assembly that has a public parameterless constructor.
    /// </summary>
    public static IReadOnlyList<IMarkItDownConverter> FromAssembly(Assembly assembly)
    {
        var converters = new List<IMarkItDownConverter>();
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || type.IsInterface || !type.IsPublic || !typeof(IMarkItDownConverter).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            if (Activator.CreateInstance(type) is IMarkItDownConverter converter)
            {
                converters.Add(converter);
            }
        }

        return converters;
    }

    /// <summary>
    /// Loads converters from <c>*.dll</c> files in <paramref name="directory"/> and from <c>&lt;name&gt;/&lt;name&gt;.dll</c> in its
    /// sub-folders. Every assembly must pass <paramref name="policy"/> before it is loaded.
    /// </summary>
    public static PluginLoadReport LoadFromDirectory(string directory, PluginTrustPolicy policy)
    {
        var results = new List<PluginLoadResult>();
        var converters = new List<IMarkItDownConverter>();
        if (!Directory.Exists(directory))
        {
            return new PluginLoadReport { Converters = converters, Results = results };
        }

        var candidates = Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateDirectories(directory)
                .Select(folder => Path.Combine(folder, Path.GetFileName(folder) + ".dll"))
                .Where(File.Exists))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var path in candidates)
        {
            var decision = policy.Evaluate(path);
            if (!decision.IsTrusted)
            {
                results.Add(new PluginLoadResult(path, false, decision.Reason, decision.Sha256, []));
                continue;
            }

            try
            {
                var context = new PluginLoadContext(path);
                var assembly = context.LoadFromAssemblyPath(path);
                var loaded = FromAssembly(assembly);
                converters.AddRange(loaded);
                results.Add(new PluginLoadResult(
                    path,
                    true,
                    loaded.Count == 0 ? "Loaded, but no converters were found." : "Loaded.",
                    decision.Sha256,
                    loaded.Select(x => x.Name).ToArray()));
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ReflectionTypeLoadException or TargetInvocationException)
            {
                results.Add(new PluginLoadResult(path, false, $"The assembly could not be loaded: {ex.Message}", decision.Sha256, []));
            }
        }

        return new PluginLoadReport { Converters = converters, Results = results };
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(x => x is not null)!;
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Shared contracts must come from the host so plugin converters implement the host's interfaces.
            if (assemblyName.Name == typeof(IMarkItDownConverter).Assembly.GetName().Name)
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}

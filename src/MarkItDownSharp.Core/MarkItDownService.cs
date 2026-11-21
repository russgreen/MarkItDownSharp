using System.Reflection;

namespace MarkItDownSharp.Core;

public sealed class MarkItDownService : IConversionHost
{
    private static readonly AsyncLocal<int> NestingDepth = new();

    private readonly IReadOnlyList<IMarkItDownConverter> _converters;
    private readonly Dictionary<IMarkItDownConverter, string> _origins = new();
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public MarkItDownService(IEnumerable<IMarkItDownConverter> converters, IEnumerable<string>? disabledConverters = null)
        : this(converters, disabledConverters, null)
    {
    }

    public MarkItDownService(
        IEnumerable<IMarkItDownConverter> converters,
        IEnumerable<string>? disabledConverters,
        IReadOnlyDictionary<IMarkItDownConverter, string>? origins)
    {
        _converters = converters.OrderBy(x => x.Priority).ToArray();
        if (origins is not null)
        {
            foreach (var pair in origins)
            {
                _origins[pair.Key] = pair.Value;
            }
        }

        foreach (var name in disabledConverters ?? [])
        {
            _disabled.Add(name);
        }

        foreach (var converter in _converters.OfType<IServiceAwareConverter>())
        {
            converter.Attach(this);
        }
    }

    /// <summary>Raised after <see cref="SetEnabled"/> changes the set of active converters.</summary>
    public event EventHandler? ConvertersChanged;

    public int Depth => NestingDepth.Value;

    public IReadOnlyCollection<string> SupportedExtensions
    {
        get
        {
            lock (_gate)
            {
                return _converters
                    .Where(IsEnabledCore)
                    .SelectMany(x => x is IExtensionProvider provider ? provider.Extensions : Array.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public IReadOnlyCollection<string> DisabledConverters
    {
        get
        {
            lock (_gate)
            {
                return _disabled.ToArray();
            }
        }
    }

    public IReadOnlyList<ConverterInfo> Describe()
    {
        lock (_gate)
        {
            return _converters.Select(converter =>
            {
                var assembly = converter.GetType().Assembly.GetName();
                return new ConverterInfo(
                    converter.Name,
                    converter.Description,
                    converter is IExtensionProvider provider ? provider.Extensions : [],
                    converter.Priority,
                    converter.RequiresNetwork,
                    IsEnabledCore(converter),
                    assembly.Name ?? "unknown",
                    converter.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? assembly.Version?.ToString() ?? "unknown",
                    _origins.TryGetValue(converter, out var origin) ? origin : "Built-in");
            }).ToArray();
        }
    }

    public bool IsEnabled(string converterName)
    {
        lock (_gate)
        {
            return !_disabled.Contains(converterName);
        }
    }

    public void SetEnabled(string converterName, bool enabled)
    {
        lock (_gate)
        {
            if (!_converters.Any(x => string.Equals(x.Name, converterName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"No converter named '{converterName}' is registered.", nameof(converterName));
            }

            var changed = enabled ? _disabled.Remove(converterName) : _disabled.Add(converterName);
            if (!changed)
            {
                return;
            }
        }

        ConvertersChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool CanConvert(string fileName)
    {
        var metadata = InferMetadata(fileName);
        lock (_gate)
        {
            return _converters.Any(x => IsEnabledCore(x) && x.CanConvert(metadata));
        }
    }

    public async Task<ConversionResult> ConvertFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ConvertCoreAsync(stream, InferMetadata(path), cancellationToken);
    }

    public Task<ConversionResult> ConvertStreamAsync(Stream stream, string fileName, CancellationToken cancellationToken = default) =>
        ConvertCoreAsync(stream, InferMetadata(fileName), cancellationToken);

    private async Task<ConversionResult> ConvertCoreAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken)
    {
        IMarkItDownConverter[] candidates;
        lock (_gate)
        {
            candidates = _converters.Where(x => IsEnabledCore(x) && x.CanConvert(metadata)).ToArray();
        }

        if (candidates.Length == 0)
        {
            throw new NotSupportedException($"No converter is registered for '{metadata.Extension ?? "unknown"}' files.");
        }

        var seekable = await EnsureSeekableAsync(stream, cancellationToken);
        var previousDepth = NestingDepth.Value;
        NestingDepth.Value = previousDepth + 1;
        try
        {
            var startPosition = seekable.Position;
            foreach (var converter in candidates)
            {
                if (converter is IContentSniffer sniffer)
                {
                    seekable.Position = startPosition;
                    var accepted = await sniffer.AcceptsContentAsync(seekable, metadata, cancellationToken);
                    seekable.Position = startPosition;
                    if (!accepted)
                    {
                        continue;
                    }
                }

                seekable.Position = startPosition;
                return await converter.ConvertAsync(seekable, metadata, cancellationToken);
            }

            throw new NotSupportedException($"No converter accepted the content of '{metadata.FileName}'.");
        }
        finally
        {
            NestingDepth.Value = previousDepth;
            if (!ReferenceEquals(seekable, stream))
            {
                await seekable.DisposeAsync();
            }
        }
    }

    private static async Task<Stream> EnsureSeekableAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            return stream;
        }

        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    private bool IsEnabledCore(IMarkItDownConverter converter) => !_disabled.Contains(converter.Name);

    private static StreamMetadata InferMetadata(string path)
    {
        var extension = Path.GetExtension(path);
        var mimeType = extension.ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".log" or ".csv" or ".tsv" => "text/plain",
            ".json" or ".ipynb" => "application/json",
            ".xml" => "application/xml",
            ".rss" => "application/rss+xml",
            ".atom" => "application/atom+xml",
            ".yaml" or ".yml" => "application/yaml",
            ".html" or ".htm" => "text/html",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".pdf" => "application/pdf",
            ".epub" => "application/epub+zip",
            ".zip" => "application/zip",
            ".msg" => "application/vnd.ms-outlook",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".webp" => "image/webp",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            _ => null
        };

        return new StreamMetadata(
            Path.GetFileName(path),
            extension,
            mimeType,
            null,
            Path.IsPathRooted(path) ? Path.GetFullPath(path) : null);
    }
}

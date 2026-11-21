using System.Text.Json.Serialization;
using MarkItDownSharp.Converters.Integrations;
using MarkItDownSharp.Core;
using MarkItDownSharp.Core.Plugins;

namespace MarkItDownSharp.Converters;

/// <summary>Optional connection to an OpenAI-compatible service (hosted or local).</summary>
public sealed class OpenAiCompatibleSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    /// <summary>Never serialised with the settings; the application stores it separately in protected form.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    public bool IsConfigured => Uri.TryCreate(Endpoint, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>Optional connection to an Azure AI resource.</summary>
public sealed class AzureSettings
{
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>Never serialised with the settings; the application stores it separately in protected form.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }
    public string? AnalyzerId { get; set; }

    public bool IsConfigured => Uri.TryCreate(Endpoint, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class ConverterSettings
{
    public List<string> DisabledConverters { get; set; } = [];
    public OpenAiCompatibleSettings ImageDescription { get; set; } = new();
    public OpenAiCompatibleSettings Transcription { get; set; } = new();
    public AzureSettings DocumentIntelligence { get; set; } = new();
    public AzureSettings ContentUnderstanding { get; set; } = new();
    public string PluginDirectory { get; set; } = string.Empty;
    public PluginTrustPolicy PluginPolicy { get; set; } = PluginTrustPolicy.DenyAll;
}

/// <summary>
/// Builds the converter set for the application: built-in converters, configured optional integrations and trusted plugins.
/// </summary>
public static class ConverterCatalog
{
    public static ConverterCatalogResult Create(ConverterSettings settings, IOcrEngine? ocr = null, HttpClient? http = null)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        IImageDescriber? describer = settings.ImageDescription.IsConfigured
            ? new OpenAiCompatibleImageDescriber(http, new Uri(settings.ImageDescription.Endpoint), settings.ImageDescription.Model, settings.ImageDescription.ApiKey)
            : null;
        ISpeechTranscriber? transcriber = settings.Transcription.IsConfigured
            ? new OpenAiCompatibleTranscriber(http, new Uri(settings.Transcription.Endpoint), settings.Transcription.Model, settings.Transcription.ApiKey)
            : null;

        var converters = new List<IMarkItDownConverter>
        {
            new TextConverter(),
            new CsvConverter(),
            new HtmlConverter(),
            new StructuredTextConverter(),
            new FeedConverter(),
            new NotebookConverter(),
            new DocxConverter(ocr),
            new ExcelConverter(ocr),
            new PptxConverter(ocr),
            new PdfConverter(ocr),
            new EpubConverter(),
            new ZipConverter(),
            new OutlookMessageConverter(),
            new ImageConverter(describer, ocr),
            new AudioConverter(transcriber)
        };

        if (settings.DocumentIntelligence.IsConfigured)
        {
            converters.Add(new AzureDocumentIntelligenceConverter(http, new Uri(settings.DocumentIntelligence.Endpoint), settings.DocumentIntelligence.ApiKey!));
        }

        if (settings.ContentUnderstanding.IsConfigured)
        {
            converters.Add(new AzureContentUnderstandingConverter(
                http,
                new Uri(settings.ContentUnderstanding.Endpoint),
                settings.ContentUnderstanding.ApiKey!,
                string.IsNullOrWhiteSpace(settings.ContentUnderstanding.AnalyzerId) ? AzureContentUnderstandingConverter.DefaultAnalyzerId : settings.ContentUnderstanding.AnalyzerId));
        }

        var origins = new Dictionary<IMarkItDownConverter, string>();
        var report = new PluginLoadReport();
        if (!string.IsNullOrWhiteSpace(settings.PluginDirectory))
        {
            report = ConverterDiscovery.LoadFromDirectory(settings.PluginDirectory, settings.PluginPolicy);
            foreach (var plugin in report.Converters)
            {
                converters.Add(plugin);
                origins[plugin] = "Plugin";
            }
        }

        var service = new MarkItDownService(converters, settings.DisabledConverters, origins);
        return new ConverterCatalogResult(service, report.Results);
    }
}

public sealed record ConverterCatalogResult(MarkItDownService Service, IReadOnlyList<PluginLoadResult> PluginResults);

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters.Integrations;

/// <summary>
/// Submits a document to an Azure AI analyze operation and polls it to completion.
/// </summary>
internal static class AzureAnalyzeOperation
{
    public static async Task<JsonObject> RunAsync(
        HttpClient http,
        Uri submitUri,
        string apiKey,
        Stream document,
        TimeSpan pollInterval,
        TimeSpan timeout,
        string serviceName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, submitUri) { Content = new StreamContent(document) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);

        Uri operationUri;
        using (var response = await http.SendAsync(request, cancellationToken))
        {
            await RemoteServiceException.ThrowIfFailedAsync(response, serviceName, cancellationToken);
            operationUri = response.Headers.TryGetValues("Operation-Location", out var values) && Uri.TryCreate(values.First(), UriKind.Absolute, out var uri)
                ? uri
                : throw new RemoteServiceException($"The {serviceName} service did not return an Operation-Location header.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                using var poll = new HttpRequestMessage(HttpMethod.Get, operationUri);
                poll.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
                using var pollResponse = await http.SendAsync(poll, deadline.Token);
                await RemoteServiceException.ThrowIfFailedAsync(pollResponse, serviceName, deadline.Token);
                var json = JsonNode.Parse(await pollResponse.Content.ReadAsStringAsync(deadline.Token))?.AsObject()
                    ?? throw new RemoteServiceException($"The {serviceName} service returned an empty response.");
                var status = json["status"]?.GetValue<string>()?.ToLowerInvariant();
                switch (status)
                {
                    case "succeeded":
                        return json;
                    case "failed" or "canceled":
                        throw new RemoteServiceException($"The {serviceName} operation {status}: {json["error"]?["message"]?.GetValue<string>()}");
                    default:
                        await Task.Delay(pollInterval, deadline.Token);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The {serviceName} operation did not finish within {timeout.TotalSeconds:0} seconds.");
        }
    }
}

/// <summary>
/// Converts documents with Azure AI Document Intelligence (prebuilt-layout, Markdown output).
/// Document content is sent to the configured Azure resource.
/// </summary>
public sealed class AzureDocumentIntelligenceConverter : IMarkItDownConverter, IExtensionProvider
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _apiKey;

    public AzureDocumentIntelligenceConverter(HttpClient http, Uri endpoint, string apiKey)
    {
        _http = http;
        _endpoint = OpenAiCompatibleImageDescriber.EnsureTrailingSlash(endpoint);
        _apiKey = apiKey;
    }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    public int Priority => -20;
    public string Name => "Azure Document Intelligence";
    public string Description => "Sends PDFs, Office files and images to Azure AI Document Intelligence for layout-aware Markdown.";
    public bool RequiresNetwork => true;
    public IReadOnlyCollection<string> Extensions { get; } =
        new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".html", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(_endpoint, "documentintelligence/documentModels/prebuilt-layout:analyze?api-version=2024-11-30&outputContentFormat=markdown");
        var result = await AzureAnalyzeOperation.RunAsync(_http, uri, _apiKey, stream, PollInterval, Timeout, "Document Intelligence", cancellationToken);
        var content = result["analyzeResult"]?["content"]?.GetValue<string>()
            ?? throw new RemoteServiceException("Document Intelligence returned no content.");
        return new ConversionResult(content.ReplaceLineEndings("\n").Trim(), Path.GetFileNameWithoutExtension(metadata.FileName));
    }
}

/// <summary>
/// Converts documents with Azure AI Content Understanding (analyzeBinary).
/// Document content is sent to the configured Azure resource.
/// </summary>
public sealed class AzureContentUnderstandingConverter : IMarkItDownConverter, IExtensionProvider
{
    public const string DefaultAnalyzerId = "prebuilt-documentAnalyzer";

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _analyzerId;

    public AzureContentUnderstandingConverter(HttpClient http, Uri endpoint, string apiKey, string analyzerId = DefaultAnalyzerId)
    {
        _http = http;
        _endpoint = OpenAiCompatibleImageDescriber.EnsureTrailingSlash(endpoint);
        _apiKey = apiKey;
        _analyzerId = analyzerId;
    }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    public int Priority => -25;
    public string Name => "Azure Content Understanding";
    public string Description => "Sends documents and images to Azure AI Content Understanding and uses its Markdown output.";
    public bool RequiresNetwork => true;
    public IReadOnlyCollection<string> Extensions { get; } =
        new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".html", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(_endpoint, $"contentunderstanding/analyzers/{Uri.EscapeDataString(_analyzerId)}:analyzeBinary?api-version=2025-05-01-preview");
        var result = await AzureAnalyzeOperation.RunAsync(_http, uri, _apiKey, stream, PollInterval, Timeout, "Content Understanding", cancellationToken);
        var contents = result["result"]?["contents"]?.AsArray()
            .Select(c => c?["markdown"]?.GetValue<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
        if (contents is null or { Count: 0 })
        {
            throw new RemoteServiceException("Content Understanding returned no Markdown content.");
        }

        var markdown = string.Join("\n\n", contents).ReplaceLineEndings("\n").Trim();
        return new ConversionResult(markdown, Path.GetFileNameWithoutExtension(metadata.FileName));
    }
}

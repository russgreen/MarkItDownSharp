using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters.Integrations;

/// <summary>
/// Describes images with any OpenAI-compatible chat-completions endpoint with vision support
/// (a hosted service, or a local server such as Ollama or LM Studio).
/// </summary>
public sealed class OpenAiCompatibleImageDescriber : IImageDescriber
{
    public const string DefaultPrompt = "Describe this image in two or three factual sentences for use as alt text.";

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly string _prompt;

    public OpenAiCompatibleImageDescriber(HttpClient http, Uri baseUri, string model, string? apiKey = null, string prompt = DefaultPrompt)
    {
        _http = http;
        _endpoint = new Uri(EnsureTrailingSlash(baseUri), "chat/completions");
        _model = model;
        _apiKey = apiKey;
        _prompt = prompt;
    }

    public async Task<string?> DescribeAsync(byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = 300,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = _prompt },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = $"data:{mimeType};base64,{Convert.ToBase64String(image)}" }
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        await RemoteServiceException.ThrowIfFailedAsync(response, "image description", cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()?.Trim();
    }

    internal static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}

/// <summary>
/// Transcribes audio with an OpenAI-compatible <c>/audio/transcriptions</c> endpoint.
/// </summary>
public sealed class OpenAiCompatibleTranscriber : ISpeechTranscriber
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;

    public OpenAiCompatibleTranscriber(HttpClient http, Uri baseUri, string model, string? apiKey = null)
    {
        _http = http;
        _endpoint = new Uri(OpenAiCompatibleImageDescriber.EnsureTrailingSlash(baseUri), "audio/transcriptions");
        _model = model;
        _apiKey = apiKey;
    }

    public async Task<string?> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_model), "model");
        form.Add(new StringContent("json"), "response_format");
        var file = new StreamContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = form };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        await RemoteServiceException.ThrowIfFailedAsync(response, "transcription", cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return json?["text"]?.GetValue<string>()?.Trim();
    }
}

public sealed class RemoteServiceException : IOException
{
    public RemoteServiceException(string message)
        : base(message)
    {
    }

    internal static async Task ThrowIfFailedAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        if (detail.Length > 300)
        {
            detail = detail[..300] + "...";
        }

        throw new RemoteServiceException($"The {operation} service returned {(int)response.StatusCode} {response.ReasonPhrase}. {detail}".Trim());
    }
}

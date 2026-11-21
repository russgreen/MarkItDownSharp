using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MarkItDownSharp.Converters;
using MarkItDownSharp.Converters.Integrations;
using MarkItDownSharp.Core;
using MarkItDownSharp.Tests.Builders;

namespace MarkItDownSharp.Tests;

public sealed class IntegrationTests
{
    private static async Task<ConversionResult> ConvertAsync(IMarkItDownConverter converter, string fileName, byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        return await new MarkItDownService([converter]).ConvertStreamAsync(stream, fileName, TestContext.Current.CancellationToken);
    }

    // ---- Images and audio ----

    [Fact]
    public async Task Image_ReportsMetadata()
    {
        var result = await ConvertAsync(new ImageConverter(), "pixel.png", DocxBuilder.OnePixelPng);

        Assert.Equal("pixel", result.Title);
        Assert.Contains("## Metadata", result.Markdown);
        Assert.Contains("Image Width", result.Markdown);
    }

    [Fact]
    public async Task Image_AddsDescriptionAndOcrWhenConfigured()
    {
        var converter = new ImageConverter(new FakeDescriber("A single red pixel."), new FakeOcr("PIXEL TEXT"));

        var result = await ConvertAsync(converter, "pixel.png", DocxBuilder.OnePixelPng);

        Assert.Contains("## Description\n\nA single red pixel.", result.Markdown);
        Assert.Contains("## Text in image\n\nPIXEL TEXT", result.Markdown);
        Assert.True(converter.RequiresNetwork);
    }

    [Fact]
    public async Task Image_StillConvertsWhenMetadataCannotBeRead()
    {
        var result = await ConvertAsync(new ImageConverter(new FakeDescriber("Described anyway")), "broken.png", [1, 2, 3, 4]);

        Assert.Contains("Described anyway", result.Markdown);
    }

    [Fact]
    public async Task Audio_ReportsPropertiesAndTranscript()
    {
        var converter = new AudioConverter(new FakeTranscriber("hello from the recording"));

        var result = await ConvertAsync(converter, "memo.wav", SilentWave(seconds: 2));

        Assert.Contains("## Metadata", result.Markdown);
        Assert.Contains("00:00:02", result.Markdown);
        Assert.Contains("8000 Hz", result.Markdown);
        Assert.Contains("## Transcript\n\nhello from the recording", result.Markdown);
    }

    [Fact]
    public async Task Audio_WorksWithoutATranscriber()
    {
        var converter = new AudioConverter();

        var result = await ConvertAsync(converter, "memo.wav", SilentWave(seconds: 1));

        Assert.DoesNotContain("Transcript", result.Markdown);
        Assert.False(converter.RequiresNetwork);
    }

    // ---- OpenAI-compatible clients ----

    [Fact]
    public async Task OpenAiDescriber_SendsImageAsDataUrlAndReadsReply()
    {
        string? requestBody = null;
        string? authorization = null;
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            authorization = request.Headers.Authorization?.ToString();
            Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.ToString());
            return Json("""{"choices":[{"message":{"content":"  A cat on a sofa. "}}]}""");
        }));
        var describer = new OpenAiCompatibleImageDescriber(http, new Uri("http://localhost:11434/v1"), "vision-model", "secret");

        var text = await describer.DescribeAsync([1, 2, 3], "image/png", TestContext.Current.CancellationToken);

        Assert.Equal("A cat on a sofa.", text);
        Assert.Equal("Bearer secret", authorization);
        var json = JsonNode.Parse(requestBody!)!;
        Assert.Equal("vision-model", json["model"]!.GetValue<string>());
        Assert.StartsWith("data:image/png;base64,", json["messages"]![0]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task OpenAiDescriber_ReportsServiceErrors()
    {
        using var http = new HttpClient(new FakeHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("bad key") })));
        var describer = new OpenAiCompatibleImageDescriber(http, new Uri("https://api.example.com/v1/"), "m");

        var error = await Assert.ThrowsAsync<RemoteServiceException>(
            () => describer.DescribeAsync([1], "image/png", TestContext.Current.CancellationToken));

        Assert.Contains("401", error.Message);
        Assert.Contains("bad key", error.Message);
    }

    [Fact]
    public async Task OpenAiTranscriber_PostsMultipartAndReadsText()
    {
        string? body = null;
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            Assert.EndsWith("/audio/transcriptions", request.RequestUri!.AbsolutePath);
            return Json("""{"text":"hello world"}""");
        }));
        var transcriber = new OpenAiCompatibleTranscriber(http, new Uri("https://api.example.com/v1"), "whisper-1");

        await using var audio = new MemoryStream([1, 2, 3]);
        var text = await transcriber.TranscribeAsync(audio, "clip.wav", TestContext.Current.CancellationToken);

        Assert.Equal("hello world", text);
        Assert.Contains("whisper-1", body);
        Assert.Contains("clip.wav", body);
    }

    // ---- Azure ----

    [Fact]
    public async Task DocumentIntelligence_SubmitsPollsAndReturnsMarkdown()
    {
        var polls = 0;
        string? submitted = null;
        string? key = null;
        using var http = new HttpClient(new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                submitted = request.RequestUri!.ToString();
                key = request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single();
                var accepted = new HttpResponseMessage(HttpStatusCode.Accepted);
                accepted.Headers.Add("Operation-Location", "https://example.cognitiveservices.azure.com/operations/1");
                return Task.FromResult(accepted);
            }

            return Task.FromResult(Json(++polls < 2
                ? """{"status":"running"}"""
                : """{"status":"succeeded","analyzeResult":{"content":"# Invoice\r\n\r\nTotal: 5"}}"""));
        }));
        var converter = new AzureDocumentIntelligenceConverter(http, new Uri("https://example.cognitiveservices.azure.com"), "azure-key")
        {
            PollInterval = TimeSpan.FromMilliseconds(1)
        };

        var result = await ConvertAsync(converter, "invoice.pdf", [1, 2, 3]);

        Assert.Equal("# Invoice\n\nTotal: 5", result.Markdown);
        Assert.Equal("azure-key", key);
        Assert.Contains("documentModels/prebuilt-layout:analyze", submitted);
        Assert.Contains("outputContentFormat=markdown", submitted);
        Assert.Equal(2, polls);
        Assert.True(converter.RequiresNetwork);
    }

    [Fact]
    public async Task DocumentIntelligence_ReportsFailedOperations()
    {
        using var http = new HttpClient(new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var accepted = new HttpResponseMessage(HttpStatusCode.Accepted);
                accepted.Headers.Add("Operation-Location", "https://example.cognitiveservices.azure.com/operations/1");
                return Task.FromResult(accepted);
            }

            return Task.FromResult(Json("""{"status":"failed","error":{"message":"Unsupported file"}}"""));
        }));
        var converter = new AzureDocumentIntelligenceConverter(http, new Uri("https://example.cognitiveservices.azure.com"), "k")
        {
            PollInterval = TimeSpan.FromMilliseconds(1)
        };

        var error = await Assert.ThrowsAsync<RemoteServiceException>(() => ConvertAsync(converter, "x.pdf", [1]));

        Assert.Contains("Unsupported file", error.Message);
    }

    [Fact]
    public async Task DocumentIntelligence_TimesOutWhenOperationNeverFinishes()
    {
        using var http = new HttpClient(new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var accepted = new HttpResponseMessage(HttpStatusCode.Accepted);
                accepted.Headers.Add("Operation-Location", "https://example.cognitiveservices.azure.com/operations/1");
                return Task.FromResult(accepted);
            }

            return Task.FromResult(Json("""{"status":"running"}"""));
        }));
        var converter = new AzureDocumentIntelligenceConverter(http, new Uri("https://example.cognitiveservices.azure.com"), "k")
        {
            PollInterval = TimeSpan.FromMilliseconds(5),
            Timeout = TimeSpan.FromMilliseconds(100)
        };

        await Assert.ThrowsAsync<TimeoutException>(() => ConvertAsync(converter, "x.pdf", [1]));
    }

    [Fact]
    public async Task ContentUnderstanding_ReturnsMarkdownFromAllContents()
    {
        string? submitted = null;
        using var http = new HttpClient(new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                submitted = request.RequestUri!.ToString();
                var accepted = new HttpResponseMessage(HttpStatusCode.Accepted);
                accepted.Headers.Add("Operation-Location", "https://example.services.ai.azure.com/operations/9");
                return Task.FromResult(accepted);
            }

            return Task.FromResult(Json("""{"status":"Succeeded","result":{"contents":[{"markdown":"first"},{"markdown":"second"}]}}"""));
        }));
        var converter = new AzureContentUnderstandingConverter(http, new Uri("https://example.services.ai.azure.com"), "k")
        {
            PollInterval = TimeSpan.FromMilliseconds(1)
        };

        var result = await ConvertAsync(converter, "scan.pdf", [1]);

        Assert.Equal("first\n\nsecond", result.Markdown);
        Assert.Contains("analyzers/prebuilt-documentAnalyzer:analyzeBinary", submitted);
    }

    [Fact]
    public async Task AzureConverters_TakePriorityOverLocalOnesWhenRegistered()
    {
        using var http = new HttpClient(new FakeHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var accepted = new HttpResponseMessage(HttpStatusCode.Accepted);
                accepted.Headers.Add("Operation-Location", "https://example.cognitiveservices.azure.com/operations/1");
                return Task.FromResult(accepted);
            }

            return Task.FromResult(Json("""{"status":"succeeded","analyzeResult":{"content":"from azure"}}"""));
        }));
        var service = ConverterCatalog.Create(
            new ConverterSettings { DocumentIntelligence = new AzureSettings { Endpoint = "https://example.cognitiveservices.azure.com", ApiKey = "k" } },
            http: http).Service;
        await using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.ConvertStreamAsync(stream, "doc.pdf", TestContext.Current.CancellationToken);
        Assert.Equal("from azure", result.Markdown);

        service.SetEnabled("Azure Document Intelligence", false);
        Assert.False(service.IsEnabled("Azure Document Intelligence"));
        await using var local = new MemoryStream(DocxBuilder.Build());
        var localResult = await service.ConvertStreamAsync(local, "a.docx", TestContext.Current.CancellationToken);
        Assert.Contains("# Heading One", localResult.Markdown);
    }

    // ---- Helpers ----

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static byte[] SilentWave(int seconds)
    {
        const int sampleRate = 8000;
        var dataLength = sampleRate * 2 * seconds;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
        return stream.ToArray();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private sealed class FakeDescriber(string text) : IImageDescriber
    {
        public Task<string?> DescribeAsync(byte[] image, string mimeType, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(text);
    }

    private sealed class FakeOcr(string text) : IOcrEngine
    {
        public Task<string?> RecognizeAsync(byte[] image, string? mimeType, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(text);
    }

    private sealed class FakeTranscriber(string text) : ISpeechTranscriber
    {
        public Task<string?> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(text);
    }
}

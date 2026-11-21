using MarkItDownSharp.Converters;
using MarkItDownSharp.Core;
using MarkItDownSharp.Core.Plugins;

namespace MarkItDownSharp.Tests;

/// <summary>A converter discovered through reflection by <see cref="ExtensibilityTests"/>.</summary>
public sealed class SamplePluginConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 5;
    public string Name => "Sample plugin";
    public string Description => "Converts .sample files.";
    public IReadOnlyCollection<string> Extensions { get; } = [".sample"];
    public bool CanConvert(StreamMetadata metadata) => metadata.Extension == ".sample";

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);
        return new ConversionResult("sample: " + await reader.ReadToEndAsync(cancellationToken));
    }
}

/// <summary>Has no parameterless constructor, so discovery must skip it.</summary>
public sealed class NeedsArgumentsConverter(string value) : IMarkItDownConverter
{
    public int Priority => 0;
    public bool CanConvert(StreamMetadata metadata) => false;
    public Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConversionResult(value));
}

public sealed class ExtensibilityTests
{
    private static readonly string TestAssemblyPath = typeof(ExtensibilityTests).Assembly.Location;

    [Fact]
    public void Discovery_FindsPublicConvertersWithParameterlessConstructors()
    {
        var converters = ConverterDiscovery.FromAssembly(typeof(ExtensibilityTests).Assembly);

        Assert.Contains(converters, c => c is SamplePluginConverter);
        Assert.DoesNotContain(converters, c => c is NeedsArgumentsConverter);
    }

    [Fact]
    public void Discovery_FindsTheBuiltInConvertersToo()
    {
        var converters = ConverterDiscovery.FromAssembly(typeof(TextConverter).Assembly);

        Assert.Contains(converters, c => c is TextConverter);
        Assert.Contains(converters, c => c is NotebookConverter);
    }

    [Fact]
    public void Service_DescribesConvertersWithCapabilityMetadata()
    {
        var service = new MarkItDownService([new SamplePluginConverter(), new TextConverter()]);

        var info = service.Describe();

        // Lower priority values come first: the sample (5) before the text converter (10).
        Assert.Equal(["Sample plugin", "Text"], info.Select(i => i.Name));
        var sample = info.Single(i => i.Name == "Sample plugin");
        Assert.Equal(5, sample.Priority);
        Assert.Equal("Converts .sample files.", sample.Description);
        Assert.Equal([".sample"], sample.Extensions);
        Assert.True(sample.IsEnabled);
        Assert.False(sample.RequiresNetwork);
    }

    [Fact]
    public void Priority_DecidesWhichConverterWins()
    {
        var first = new Fixed("first", priority: 1);
        var second = new Fixed("second", priority: 2);
        var service = new MarkItDownService([second, first]);

        var result = service.ConvertStreamAsync(new MemoryStream(), "a.fix", TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        Assert.Equal("first", result.Markdown);
    }

    [Fact]
    public async Task DisablingAConverterRemovesItsFormatsAndFallsBack()
    {
        var first = new Fixed("first", priority: 1);
        var second = new Fixed("second", priority: 2);
        var service = new MarkItDownService([first, second]);
        var changes = 0;
        service.ConvertersChanged += (_, _) => changes++;

        service.SetEnabled("first", false);
        var result = await service.ConvertStreamAsync(new MemoryStream(), "a.fix", TestContext.Current.CancellationToken);

        Assert.Equal("second", result.Markdown);
        Assert.Equal(1, changes);
        Assert.Contains(service.Describe(), i => i.Name == "first" && !i.IsEnabled);

        service.SetEnabled("second", false);
        Assert.DoesNotContain(".fix", service.SupportedExtensions);
        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.ConvertStreamAsync(new MemoryStream(), "a.fix", TestContext.Current.CancellationToken));

        service.SetEnabled("second", true);
        Assert.Contains(".fix", service.SupportedExtensions);
    }

    [Fact]
    public void SetEnabled_RejectsUnknownConverters()
    {
        var service = new MarkItDownService([new TextConverter()]);

        Assert.Throws<ArgumentException>(() => service.SetEnabled("Nope", false));
    }

    [Fact]
    public void DisabledConvertersCanBeSuppliedUpFront()
    {
        var service = new MarkItDownService([new TextConverter()], ["Text"]);

        Assert.Empty(service.SupportedExtensions);
        Assert.Equal(["Text"], service.DisabledConverters);
    }

    // ---- Plugin trust ----

    [Fact]
    public void Trust_RefusesEverythingByDefault()
    {
        var decision = PluginTrustPolicy.DenyAll.Evaluate(TestAssemblyPath);

        Assert.False(decision.IsTrusted);
        Assert.Contains("disabled", decision.Reason);
    }

    [Fact]
    public void Trust_RefusesUnknownUnsignedAssemblies()
    {
        var policy = new PluginTrustPolicy { Enabled = true };

        var decision = policy.Evaluate(TestAssemblyPath);

        Assert.False(decision.IsTrusted);
        Assert.NotNull(decision.Sha256);
    }

    [Fact]
    public void Trust_AcceptsPinnedHashesOnly()
    {
        var hash = AssemblyHasher.Sha256Hex(TestAssemblyPath);

        Assert.True(new PluginTrustPolicy { Enabled = true, TrustedSha256 = [hash.ToLowerInvariant()] }.Evaluate(TestAssemblyPath).IsTrusted);
        Assert.False(new PluginTrustPolicy { Enabled = true, TrustedSha256 = [new string('0', 64)] }.Evaluate(TestAssemblyPath).IsTrusted);
        Assert.False(new PluginTrustPolicy { Enabled = false, TrustedSha256 = [hash] }.Evaluate(TestAssemblyPath).IsTrusted);
    }

    [Fact]
    public void Trust_RejectsUnsignedAssembliesWhenSignaturesAreRequired()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Authenticode verification is Windows-only.");
        var policy = new PluginTrustPolicy { Enabled = true, TrustedSignerThumbprints = ["0000000000000000000000000000000000000000"] };

        var decision = policy.Evaluate(TestAssemblyPath);

        Assert.False(decision.IsTrusted);
        Assert.Contains("signature", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authenticode_AcceptsMicrosoftSignedAssemblyAndReportsSigner()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Authenticode verification is Windows-only.");
        var signed = typeof(object).Assembly.Location;

        var result = AuthenticodeVerifier.Verify(signed);
        Assert.SkipUnless(result.IsValid, $"The runtime assembly is not Authenticode-signed on this machine ({result.Reason}).");

        Assert.NotEmpty(result.Thumbprints);
        var policy = new PluginTrustPolicy { Enabled = true, TrustedSignerThumbprints = [result.Thumbprints[0]] };
        Assert.True(policy.Evaluate(signed).IsTrusted);
    }

    [Fact]
    public void Plugins_AreLoadedOnlyWhenTrusted()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MarkItDownSharpTests", Guid.NewGuid().ToString("N"), "plugins");
        Directory.CreateDirectory(folder);
        var plugin = Path.Combine(folder, "SamplePlugin.dll");
        File.Copy(TestAssemblyPath, plugin);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "ignored");

        var refused = ConverterDiscovery.LoadFromDirectory(folder, PluginTrustPolicy.DenyAll);
        Assert.Empty(refused.Converters);
        Assert.Single(refused.Results);
        Assert.False(refused.Results[0].Loaded);

        var policy = new PluginTrustPolicy { Enabled = true, TrustedSha256 = [AssemblyHasher.Sha256Hex(plugin)] };
        var loaded = ConverterDiscovery.LoadFromDirectory(folder, policy);

        Assert.True(loaded.Results[0].Loaded);
        Assert.Contains(loaded.Converters, c => c.Name == "Sample plugin");
        Assert.Contains("Sample plugin", loaded.Results[0].Converters);
    }

    [Fact]
    public async Task Catalog_RegistersTrustedPluginsAndLabelsTheirOrigin()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MarkItDownSharpTests", Guid.NewGuid().ToString("N"), "plugins");
        Directory.CreateDirectory(folder);
        var plugin = Path.Combine(folder, "SamplePlugin.dll");
        File.Copy(TestAssemblyPath, plugin);
        var settings = new ConverterSettings
        {
            PluginDirectory = folder,
            PluginPolicy = new PluginTrustPolicy { Enabled = true, TrustedSha256 = [AssemblyHasher.Sha256Hex(plugin)] }
        };

        var catalog = ConverterCatalog.Create(settings);

        Assert.Contains(catalog.Service.Describe(), i => i.Name == "Sample plugin" && i.Origin == "Plugin");
        Assert.Contains(catalog.Service.Describe(), i => i.Name == "Text" && i.Origin == "Built-in");
        await using var stream = new MemoryStream("hi"u8.ToArray());
        var result = await catalog.Service.ConvertStreamAsync(stream, "a.sample", TestContext.Current.CancellationToken);
        Assert.Equal("sample: hi", result.Markdown);
    }

    [Fact]
    public void Plugins_MissingFolderYieldsEmptyReport()
    {
        var report = ConverterDiscovery.LoadFromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), PluginTrustPolicy.DenyAll);

        Assert.Empty(report.Converters);
        Assert.Empty(report.Results);
    }

    private sealed class Fixed(string text, int priority) : IMarkItDownConverter, IExtensionProvider
    {
        public int Priority => priority;
        public string Name => text;
        public IReadOnlyCollection<string> Extensions { get; } = [".fix"];
        public bool CanConvert(StreamMetadata metadata) => metadata.Extension == ".fix";
        public Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConversionResult(text));
    }
}

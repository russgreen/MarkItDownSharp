using System.Buffers.Binary;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MarkItDownSharp.Tests;

/// <summary>
/// Checks the MSIX manifest, package identity and visual assets so mistakes are caught on any machine,
/// before the Windows packaging step.
/// </summary>
public sealed partial class PackagingTests
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    private static readonly string DesktopFolder = RepositoryFiles.Path("src", "MarkItDownSharp.Desktop");

    private static XDocument LoadManifest() => XDocument.Load(Path.Combine(DesktopFolder, "Package.appxmanifest"));

    [Fact]
    public void Identity_FollowsPackageNamingRules()
    {
        var identity = LoadManifest().Root!.Element(Foundation + "Identity")!;

        Assert.Matches(@"^[A-Za-z0-9][A-Za-z0-9.\-]{2,49}$", identity.Attribute("Name")!.Value);
        Assert.Matches(@"^CN=.+", identity.Attribute("Publisher")!.Value);
        // The Microsoft Store reserves the last version part, so it must stay 0.
        Assert.Matches(@"^\d+\.\d+\.\d+\.0$", identity.Attribute("Version")!.Value);
    }

    [Fact]
    public void ManifestVersion_MatchesTheBuildVersion()
    {
        var props = XDocument.Load(RepositoryFiles.Path("Directory.Build.props"));
        var version = props.Descendants("Version").Single().Value;
        var manifestVersion = LoadManifest().Root!.Element(Foundation + "Identity")!.Attribute("Version")!.Value;

        Assert.Equal(version + ".0", manifestVersion);
    }

    [Fact]
    public void DisplayNames_AreConsistent()
    {
        var root = LoadManifest().Root!;
        var visual = root.Descendants(Uap + "VisualElements").Single();

        Assert.Equal("MarkItDownSharp", root.Element(Foundation + "Properties")!.Element(Foundation + "DisplayName")!.Value);
        Assert.Equal("MarkItDownSharp", visual.Attribute("DisplayName")!.Value);
        Assert.False(string.IsNullOrWhiteSpace(visual.Attribute("Description")!.Value));
    }

    [Theory]
    [InlineData("Square44x44Logo", 44, 44)]
    [InlineData("Square150x150Logo", 150, 150)]
    public void VisualElementLogos_ExistWithTheRightSize(string attribute, int width, int height)
    {
        var visual = LoadManifest().Root!.Descendants(Uap + "VisualElements").Single();

        AssertPng(visual.Attribute(attribute)!.Value, width, height);
    }

    [Fact]
    public void StoreLogo_SmallTileAndSplashScreen_ExistWithTheRightSize()
    {
        var root = LoadManifest().Root!;

        AssertPng(root.Element(Foundation + "Properties")!.Element(Foundation + "Logo")!.Value, 50, 50);
        AssertPng(root.Descendants(Uap + "DefaultTile").Single().Attribute("Square71x71Logo")!.Value, 71, 71);
        AssertPng(root.Descendants(Uap + "SplashScreen").Single().Attribute("Image")!.Value, 620, 300);
    }

    [Fact]
    public void EveryAssetInTheAssetsFolder_IsAValidImage()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(DesktopFolder, "Assets"), "*.png"))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.True(bytes.AsSpan().StartsWith(PngSignature), $"{Path.GetFileName(file)} is not a PNG file.");
        }
    }

    [Fact]
    public void ApplicationIcon_IsAValidIconAndReferencedByTheProject()
    {
        var project = XDocument.Load(Path.Combine(DesktopFolder, "MarkItDownSharp.Desktop.csproj"));
        var iconPath = project.Descendants("ApplicationIcon").Single().Value.Replace('\\', Path.DirectorySeparatorChar);
        var bytes = File.ReadAllBytes(Path.Combine(DesktopFolder, iconPath));

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)));
        Assert.True(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2)) >= 1, "The icon has no images.");
    }

    [Fact]
    public void TargetVersions_AreOrdered()
    {
        var family = LoadManifest().Root!.Descendants(Foundation + "TargetDeviceFamily").Single();

        var min = Version.Parse(family.Attribute("MinVersion")!.Value);
        var tested = Version.Parse(family.Attribute("MaxVersionTested")!.Value);

        Assert.True(min <= tested);
    }

    [Fact]
    public void Capabilities_AreLimitedToWhatTheAppNeeds()
    {
        var capabilities = LoadManifest().Root!.Element(Foundation + "Capabilities")!.Elements().ToList();

        // Full trust is required to watch arbitrary folders; nothing else should be requested.
        var capability = Assert.Single(capabilities);
        Assert.Equal(Rescap + "Capability", capability.Name);
        Assert.Equal("runFullTrust", capability.Attribute("Name")!.Value);
    }

    [Fact]
    public void StartupTask_MatchesTheIdUsedByTheApplication()
    {
        var task = LoadManifest().Root!.Descendants(Uap5 + "StartupTask").Single();
        var source = File.ReadAllText(Path.Combine(DesktopFolder, "Services", "WindowsShellServices.cs"));
        var id = StartupTaskId().Match(source).Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(id, task.Attribute("TaskId")!.Value);
        Assert.Equal("false", task.Attribute("Enabled")!.Value);
    }

    [Fact]
    public void ReleaseScripts_ExistForSigningUpgradeAndInstallChecks()
    {
        foreach (var script in new[] { "Build-Msix.ps1", "Test-MsixInstall.ps1", "Test-MsixUpgrade.ps1", "New-TestCertificate.ps1" })
        {
            Assert.True(File.Exists(RepositoryFiles.Path("build", script)), $"build/{script} is missing.");
        }
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static void AssertPng(string manifestPath, int width, int height)
    {
        var path = Path.Combine(DesktopFolder, manifestPath.Replace('\\', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"The manifest refers to '{manifestPath}', which does not exist.");

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().StartsWith(PngSignature), $"'{manifestPath}' is not a PNG file.");
        Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    [GeneratedRegex("StartupTaskId\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex StartupTaskId();
}

using System.Xml.Linq;

namespace MarkItDownSharp.Tests;

/// <summary>
/// Static checks on the XAML that guard theme support (light, dark, high contrast) and accessibility.
/// They cannot replace a manual run with Narrator, but they stop the common regressions.
/// </summary>
public sealed class XamlLintTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Automation = "using:Microsoft.UI.Xaml.Automation";

    private static readonly string[] ColourAttributes = ["Foreground", "Background", "BorderBrush", "Fill", "Stroke", "Color", "SelectionHighlightColor"];

    private static readonly string[] InteractiveControls =
    [
        "Button", "ToggleButton", "CheckBox", "RadioButton", "ComboBox", "TextBox", "PasswordBox", "NumberBox",
        "ToggleSwitch", "ListView", "SelectorBar", "SelectorBarItem", "Slider"
    ];

    public static TheoryData<string> XamlFiles => new() { "MainWindow.xaml", "App.xaml" };

    private static XDocument Load(string file) =>
        XDocument.Load(RepositoryFiles.Path("src", "MarkItDownSharp.Desktop", file), LoadOptions.SetLineInfo);

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void Colours_AreNeverHardCoded(string file)
    {
        var offenders = Load(file).Descendants()
            .SelectMany(e => e.Attributes().Where(a => ColourAttributes.Contains(a.Name.LocalName) && !a.Value.StartsWith('{')).Select(a => Describe(e, a)))
            .ToList();

        Assert.True(offenders.Count == 0, "Hard-coded colours break dark and high-contrast themes. Use {ThemeResource ...}: " + string.Join("; ", offenders));
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void Brushes_ComeFromThemeResources(string file)
    {
        var offenders = Load(file).Descendants()
            .SelectMany(e => e.Attributes()
                .Where(a => ColourAttributes.Contains(a.Name.LocalName) && a.Value.StartsWith("{StaticResource", StringComparison.Ordinal))
                .Select(a => Describe(e, a)))
            .ToList();

        // StaticResource brushes are resolved once and do not follow theme or high-contrast changes.
        Assert.True(offenders.Count == 0, "Use ThemeResource for brushes: " + string.Join("; ", offenders));
    }

    [Fact]
    public void EveryInteractiveControl_HasAnAutomationName()
    {
        var missing = Load("MainWindow.xaml").Descendants()
            .Where(e => e.Name.Namespace == Presentation && InteractiveControls.Contains(e.Name.LocalName))
            .Where(e => e.Attributes().All(a => a.Name.LocalName != "AutomationProperties.Name"))
            .Select(e => $"{e.Name.LocalName} at line {((System.Xml.IXmlLineInfo)e).LineNumber}")
            .ToList();

        Assert.True(missing.Count == 0, "Screen readers need AutomationProperties.Name on: " + string.Join("; ", missing));
    }

    [Fact]
    public void InteractiveControls_StayInTheKeyboardTabOrder()
    {
        var removed = Load("MainWindow.xaml").Descendants()
            .Where(e => InteractiveControls.Contains(e.Name.LocalName))
            .Where(e => string.Equals((string?)e.Attribute("IsTabStop"), "False", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(removed);
    }

    [Fact]
    public void PrimaryActions_HaveKeyboardShortcuts()
    {
        var accelerators = Load("MainWindow.xaml").Descendants(Presentation + "KeyboardAccelerator")
            .Select(a => ((string?)a.Attribute("Modifiers") ?? string.Empty, (string)a.Attribute("Key")!))
            .ToList();

        Assert.Contains(("", "F5"), accelerators);
        Assert.Contains(("Shift", "F5"), accelerators);
        Assert.Contains(("Control", "P"), accelerators);
        Assert.Contains(("Control", "R"), accelerators);
        Assert.Contains(("Control", "S"), accelerators);
    }

    [Fact]
    public void SectionTitles_AreMarkedAsHeadings()
    {
        var missing = Load("MainWindow.xaml").Descendants(Presentation + "TextBlock")
            .Where(t => ((string?)t.Attribute("Style") ?? string.Empty).Contains("SubtitleTextBlockStyle", StringComparison.Ordinal) ||
                        ((string?)t.Attribute("Style") ?? string.Empty).Contains("TitleTextBlockStyle", StringComparison.Ordinal))
            .Where(t => t.Attributes().All(a => a.Name.LocalName != "AutomationProperties.HeadingLevel"))
            .Select(t => $"line {((System.Xml.IXmlLineInfo)t).LineNumber}")
            .ToList();

        Assert.True(missing.Count == 0, "Headings need AutomationProperties.HeadingLevel: " + string.Join("; ", missing));
    }

    [Fact]
    public void ProgressAndStatus_AreAnnouncedToScreenReaders()
    {
        var status = Load("MainWindow.xaml").Descendants(Presentation + "TextBlock")
            .Single(t => ((string?)t.Attribute("Text") ?? string.Empty).Contains("ViewModel.Status", StringComparison.Ordinal));

        Assert.Equal("Polite", (string?)status.Attribute("AutomationProperties.LiveSetting"));
    }

    [Fact]
    public void XamlFilesDoNotUseNamedColourElements()
    {
        foreach (var file in new[] { "MainWindow.xaml", "App.xaml" })
        {
            var colours = Load(file).Descendants().Where(e => e.Name.LocalName is "SolidColorBrush" or "Color").ToList();
            Assert.Empty(colours);
        }
    }

    private static string Describe(XElement element, XAttribute attribute) =>
        $"{element.Name.LocalName}.{attribute.Name.LocalName}=\"{attribute.Value}\" (line {((System.Xml.IXmlLineInfo)element).LineNumber})";
}

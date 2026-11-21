using MarkItDownSharp.Worker;

namespace MarkItDownSharp.Tests;

public sealed class SettingsValidatorTests
{
    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "MarkItDownSharpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void ValidFolders_ProduceNoIssues()
    {
        var root = NewRoot();
        var input = Directory.CreateDirectory(Path.Combine(root, "in")).FullName;
        var output = Path.Combine(root, "out");

        Assert.Empty(MonitorSettingsValidator.Validate(input, output));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyFolders_AreErrorsForBothFields(string? value)
    {
        var issues = MonitorSettingsValidator.Validate(value, value);

        Assert.Contains(issues, i => i.Field == MonitorSettingsValidator.InputField && i.Severity == ValidationSeverity.Error);
        Assert.Contains(issues, i => i.Field == MonitorSettingsValidator.OutputField && i.Severity == ValidationSeverity.Error);
        Assert.True(MonitorSettingsValidator.HasErrors(issues));
    }

    [Fact]
    public void MissingInputFolder_ExplainsWhichFolderIsMissing()
    {
        var root = NewRoot();
        var missing = Path.Combine(root, "nope");

        var issues = MonitorSettingsValidator.Validate(missing, Path.Combine(root, "out"));

        var issue = Assert.Single(issues);
        Assert.Equal(MonitorSettingsValidator.InputField, issue.Field);
        Assert.Contains(missing, issue.Message);
        Assert.Contains("does not exist", issue.Message);
    }

    [Fact]
    public void FileInsteadOfFolder_IsAnError()
    {
        var root = NewRoot();
        var file = Path.Combine(root, "file.txt");
        File.WriteAllText(file, "x");

        var asInput = MonitorSettingsValidator.Validate(file, Path.Combine(root, "out"));
        var asOutput = MonitorSettingsValidator.Validate(root, file);

        Assert.Contains(asInput, i => i.Field == MonitorSettingsValidator.InputField && i.Message.Contains("is a file"));
        Assert.Contains(asOutput, i => i.Field == MonitorSettingsValidator.OutputField && i.Message.Contains("is a file"));
    }

    [Fact]
    public void SameFolder_IsAnError()
    {
        var root = NewRoot();

        var issues = MonitorSettingsValidator.Validate(root, root + Path.DirectorySeparatorChar);

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("different"));
    }

    [Fact]
    public void OutputInsideInput_IsAWarningNotAnError()
    {
        var root = NewRoot();

        var issues = MonitorSettingsValidator.Validate(root, Path.Combine(root, "converted"));

        var issue = Assert.Single(issues);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.False(MonitorSettingsValidator.HasErrors(issues));
    }

    [Fact]
    public void InvalidPathCharacters_AreReported()
    {
        var root = NewRoot();

        var issues = MonitorSettingsValidator.Validate(root, "C:\\bad\0path");

        Assert.Contains(issues, i => i.Field == MonitorSettingsValidator.OutputField && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void ReadOnlyOutputFolder_ReportsPermissionProblem()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses a Windows ACL deny rule to simulate a read-only folder.");
        var root = NewRoot();
        var input = Directory.CreateDirectory(Path.Combine(root, "in")).FullName;
        var locked = Directory.CreateDirectory(Path.Combine(root, "locked"));
        var security = locked.GetAccessControl();
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            identity,
            System.Security.AccessControl.FileSystemRights.WriteData | System.Security.AccessControl.FileSystemRights.CreateFiles,
            System.Security.AccessControl.AccessControlType.Deny);
        security.AddAccessRule(rule);
        locked.SetAccessControl(security);
        try
        {
            var issues = MonitorSettingsValidator.Validate(input, Path.Combine(locked.FullName, "out"));

            var issue = Assert.Single(issues);
            Assert.Equal(MonitorSettingsValidator.OutputField, issue.Field);
            Assert.Contains("permission", issue.Message);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            locked.SetAccessControl(security);
        }
    }
}

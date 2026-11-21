using Fallout.Common;
using Fallout.Common.Git;
using Fallout.Common.Tools.DotNet;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

// Builds the WiX installer from the signed publish folder, then signs the MSI.
partial class Build
{
    Target Installer => _ => _
        .DependsOn(Publish)
        .TriggeredBy(Sign)
        .OnlyWhenStatic(() => GitRepository.IsOnMainOrMasterBranch())
        .Executes(() =>
        {
            // Version home: Directory.Build.props (<Version>), the same value the wixproj falls back to.
            var version = GetProjectVersion(Path.Combine(RootDirectory, "Directory.Build.props"));
            var wixProject = Path.Combine(RootDirectory, "installer", "MarkItDownSharp.Setup.wixproj");

            Log.Information("WiX project : {wixProject}", wixProject);
            Log.Information("Version : {version}", version);

            // WiX 7 only: the wixproj carries <AcceptEula>wix7</AcceptEula>, which is what lets this build run on CI.
            // Error WIX7015 here means that property is missing from the wixproj.
            // Always pass ProductVersion so the MSI name, the Package version and the signing glob can never disagree.
            DotNetBuild(settings => settings
                .SetProjectFile(wixProject)
                .SetConfiguration("Release")
                .SetProperty("ProductVersion", version)
                // The signed, unpackaged publish folder the MSI installs (trailing separator required by the wxs).
                .SetProperty("PublishRoot", PublishDirectory + Path.DirectorySeparatorChar.ToString())
                .SetVerbosity(DotNetVerbosity.minimal));

            SignMSI(Path.Combine(RootDirectory, "installer", "MarkItDownSharp-SetupFiles"), version);
        });

    static string GetProjectVersion(string projectFilePath)
    {
        var doc = XDocument.Load(projectFilePath);
        var propertyGroup = doc.Root?
            .Elements()
            .FirstOrDefault(x => x.Name.LocalName == "PropertyGroup");

        var versionProperties = new[] { "Version", "VersionPrefix", "ApplicationVersion", "FileVersion", "InformationalVersion" };

        var versionElement = propertyGroup?
            .Elements()
            .FirstOrDefault(x => versionProperties.Contains(x.Name.LocalName) && !string.IsNullOrWhiteSpace(x.Value));

        return versionElement?.Value ?? "1.0.0";
    }

    static void SignMSI(string setupFilesDirectory, string version)
    {
        // A missing MSI after a successful build is a failure, never a silent skip.
        var msiPath = Directory.GetFiles(setupFilesDirectory, $"*.{version}.msi").FirstOrDefault()
            ?? throw new FileNotFoundException($"No *.{version}.msi found in {setupFilesDirectory} to sign.");

        Log.Information("Signing {msiPath}", msiPath);
        SignFiles(new System.Collections.Generic.List<string> { msiPath });
    }
}

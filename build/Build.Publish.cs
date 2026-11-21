using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tools.DotNet;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

partial class Build
{
    /// <summary>
    /// Publishes the desktop app without MSIX package identity and with the Windows App SDK runtime bundled, which is
    /// what an MSI can install. It builds into its own artifacts folder so it never reuses the packaged Release build.
    /// </summary>
    Target Publish => _ => _
        .TriggeredBy(Test)
        .Executes(() =>
        {
            var artifacts = OutputDirectory / "obj";

            PublishDirectory.CreateOrCleanDirectory();

            DotNetPublish(settings => settings
                .SetProject(DesktopProject)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("win-x64")
                .EnableSelfContained()
                .SetOutput(PublishDirectory)
                .SetProperty("Platform", "x64")
                // EnableMsixTooling stays on (from the csproj): it is what generates the .pri resource index an unpackaged app needs.
                .SetProperty("WindowsPackageType", "None")
                .SetProperty("UseArtifactsOutput", "true")
                .SetProperty("ArtifactsPath", artifacts.ToString())
                .SetVerbosity(DotNetVerbosity.minimal));
        });
}

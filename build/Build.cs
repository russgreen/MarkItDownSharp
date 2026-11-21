using Fallout.Common;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Solutions;

/// <summary>
/// Release pipeline: Clean -> Compile -> Test -> Publish -> Sign -> Installer.
/// Sign and Installer only run on the main branch. Run `./build.ps1` for the whole chain.
/// </summary>
partial class Build : FalloutBuild
{
    readonly AbsolutePath OutputDirectory = RootDirectory / "output";
    readonly AbsolutePath SourceDirectory = RootDirectory / "src";
    readonly AbsolutePath TestsDirectory = RootDirectory / "tests";

    /// <summary>The unpackaged, self-contained desktop app. Signed in place, then harvested by the WiX installer.</summary>
    AbsolutePath PublishDirectory => OutputDirectory / "MarkItDownSharp";

    AbsolutePath DesktopProject => SourceDirectory / "MarkItDownSharp.Desktop" / "MarkItDownSharp.Desktop.csproj";
    AbsolutePath TestProject => TestsDirectory / "MarkItDownSharp.Tests" / "MarkItDownSharp.Tests.csproj";

    [GitRepository]
    [Required]
    readonly GitRepository GitRepository;

    [Solution]
    [Required]
    readonly Solution Solution;

    public static int Main() => Execute<Build>(x => x.Clean);
}

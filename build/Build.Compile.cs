using Fallout.Common;
using Fallout.Common.Tools.DotNet;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

partial class Build
{
    Target Compile => _ => _
        .TriggeredBy(Clean)
        .Executes(() =>
        {
            DotNetRestore(settings => settings
                .SetProjectFile(Solution));

            DotNetBuild(settings => settings
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration.Release)
                .EnableNoRestore()
                .SetVerbosity(DotNetVerbosity.minimal));
        });

    Target Test => _ => _
        .TriggeredBy(Compile)
        .Executes(() =>
        {
            DotNetTest(settings => settings
                .SetProjectFile(TestProject)
                .SetConfiguration(Configuration.Release)
                .EnableNoRestore()
                .EnableNoBuild()
                .SetVerbosity(DotNetVerbosity.minimal));
        });
}

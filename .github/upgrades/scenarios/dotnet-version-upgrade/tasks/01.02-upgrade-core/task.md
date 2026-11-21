# 01.02-upgrade-core: Retarget the Core leaf project

## Objective
Retarget MarkItDownSharp.Core from net8.0 to net11.0 as the leaf project in the dependency graph.

## Scope
Apply the target-framework replacement only to src/MarkItDownSharp.Core/MarkItDownSharp.Core.csproj. Review the existing behavioral advisories already recorded in the parent task; no code change is expected because their triggers are absent. Build the upgraded Core project.

## Done when
Core targets net11.0, restores, and builds with zero warnings or errors and its output is produced under net11.0.

## Research Findings

- `TargetFramework` is defined directly in the Core project as `net8.0`; no `Directory.Build.props`, package references, imported local targets, or conditional properties override it.
- Core is the leaf dependency used by Converters, Worker, Desktop, and Tests. It has two `NotSupportedException` advisories, but both sites explicitly throw the exception for unsupported inputs; the .NET 11 behavioral triggers involve framework APIs and are not applicable.
- Build this SDK-style class library with `dotnet build`, then confirm the output folder is `net11.0`.

# 01-upgrade-solution: Upgrade the solution to .NET 11 and SLNX

Verify the required .NET 11 toolchain and any `global.json` constraints, then add Central Package Management and update the Core, Converters, Worker, Desktop, and Tests projects atomically. Retarget the cross-platform projects to `net11.0`, retarget Desktop to the assessment's .NET 11 Windows target, update package references including the incompatible Windows App SDK and deprecated xUnit package, and address any compilation or behavior-sensitive compatibility issues identified by the assessment.

Convert the existing `MarkItDownSharp.sln` to `MarkItDownSharp.slnx`, remove the obsolete `.sln` file, and ensure all repository tooling references the new solution format. Preserve Windows packaging behavior and project relationships while applying the framework and package changes.

**Done when**: the .NET 11 SDK is validated, all five projects target the intended .NET 11 frameworks, package versions are centralized and compatible, the solution is represented only by `MarkItDownSharp.slnx`, repository tooling uses the SLNX file, the whole solution builds with zero warnings or errors, and the full existing test suite passes.

## Scope Inventory

- **Projects**: `MarkItDownSharp.Core` (leaf); `MarkItDownSharp.Converters` and `MarkItDownSharp.Worker` (depend on Core); `MarkItDownSharp.Desktop` and `MarkItDownSharp.Tests` (depend on all three libraries).
- **Concerns**: .NET 11 targeting, Central Package Management, Windows App SDK 2.7.0 compatibility, xUnit v3 package replacement, existing build warnings, solution migration, CI/docs references, and final build/test validation.
- **Toolchain**: a compatible .NET 11 SDK is installed. No `global.json`, `Directory.Build.*`, or existing `Directory.Packages.props` is present.
- **Package audit**: no version conflicts or conditional/MSBuild-property versions were found. Desktop uses CommunityToolkit.Mvvm 8.4.0 and Microsoft.WindowsAppSDK 1.6.250108002; Tests uses coverlet.collector 6.0.4, Microsoft.NET.Test.Sdk 17.13.0, xunit 2.9.3, and xunit.runner.visualstudio 3.0.2.
- **Assessment actions**: all five projects require TFM changes; Desktop requires Microsoft.WindowsAppSDK 2.7.0; Tests has a deprecated xunit v2 package and should migrate to `xunit.v3` while retaining the Visual Studio runner.
- **Baseline**: the .NET 8 solution builds, but with ten warnings in Desktop: nine MVVMTK0045 warnings from source-generated observable fields and NETSDK1198 for a missing `win-x64` publish profile. These must be fixed, not suppressed.
- **Solution/tooling references**: `README.md` and `.github/workflows/windows-build.yml` refer to `MarkItDownSharp.sln`; CI also pins .NET 8. The installed .NET 11 CLI provides `dotnet sln MarkItDownSharp.sln migrate`.
- **Stubs**: no `// STUB:` markers were found.

## Behavioral API review

- **MemoryStream maximum capacity**: .NET 11 changes the exception for capacity/length values beyond `0x7FFFFFC7`. The affected test call sites create small in-memory streams and never set capacity or length near the limit. Official reference: https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/memorystream-max-capacity. Disposition: not applicable; existing tests are sufficient.
- **NotSupportedException notices**: the Core call sites deliberately throw a new `NotSupportedException` for unsupported file extensions. The .NET 11 notices apply to framework APIs that now throw this exception under specific reflection or stack-trace configurations; neither trigger is present. Disposition: not applicable; no code change required.

## Decomposition

The common upgrade hint requires dependency-ordered processing for this five-project graph. Use four subtasks: prepare CPM/SLNX infrastructure on the current TFM, upgrade the Core leaf, upgrade the Converters/Worker dependents, then upgrade Desktop/Tests and perform final whole-solution validation.

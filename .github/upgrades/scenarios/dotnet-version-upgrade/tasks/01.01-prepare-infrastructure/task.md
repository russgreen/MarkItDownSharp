# 01.01-prepare-infrastructure: Prepare CPM and SLNX infrastructure

## Objective
Resolve the pre-existing build warnings, centralize the existing six package versions in Directory.Packages.props without changing versions, migrate MarkItDownSharp.sln to MarkItDownSharp.slnx, and update repository references that name the solution file. Build and validate the current .NET 8 solution after these infrastructure changes.

## Scope
- Fix MVVMTK0045 warnings in Desktop by converting observable fields to partial properties.
- Remove or replace the invalid PublishProfile setting so NETSDK1198 is eliminated without changing packaging behavior.
- Create Directory.Packages.props with ManagePackageVersionsCentrally enabled and current package versions only. Remove Version attributes from PackageReference items.
- Migrate the solution using the .NET CLI and update README and workflow references to MarkItDownSharp.slnx.
- Capture post-infrastructure package resolution against the baseline artifact.

## Done when
The current .NET 8 project graph builds with zero warnings, package resolution remains version-neutral, CPM owns every package version, the obsolete .sln file is removed, and repository solution references use MarkItDownSharp.slnx.

## Research Findings

- The baseline `.NET 8` solution succeeds but emits nine MVVMTK0045 diagnostics in `JobItemViewModel.cs` and `MainWindowViewModel.cs`, plus NETSDK1198 because the project names a nonexistent `win-x64` publish profile.
- No `Directory.Packages.props`, version properties, conditional package references, shared imported package references, or version conflicts exist. The six current direct package versions are captured in `artifacts/cpm/baseline-packages.json`.
- `CommunityToolkit.Mvvm` 8.4.0 supports source-generated partial properties, so each annotated field can be replaced by a public partial property while preserving generated names and notification behavior.
- The .NET 11 CLI can migrate the solution with `dotnet sln MarkItDownSharp.sln migrate`; README and Windows CI are the only active repository references to the `.sln` name.
- Use `dotnet build` for this SDK-style/WinUI solution and keep the normal Debug configuration. No VS-only resources or legacy project formats were detected.

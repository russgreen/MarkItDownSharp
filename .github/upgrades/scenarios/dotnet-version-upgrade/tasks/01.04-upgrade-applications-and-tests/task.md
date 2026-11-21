# 01.04-upgrade-applications-and-tests: Upgrade Desktop, tests, and final tooling

## Objective
Retarget MarkItDownSharp.Desktop and MarkItDownSharp.Tests, update required test/Windows packages, align CI with .NET 11, and complete final whole-solution validation.

## Scope
- Change Desktop to net11.0-windows10.0.22000.0 and update the centrally managed Microsoft.WindowsAppSDK version to 2.7.0.
- Change Tests to net11.0 and replace deprecated xunit with xunit.v3, retaining the runner and adapting only package usage that migration evidence requires.
- Update the Windows CI workflow to install the .NET 11 SDK and keep SLNX usage.
- Preserve Windows application packaging and project references. Review the recorded MemoryStream and NotSupportedException behavioral dispositions.
- Build the full SLNX solution warning-free and run the complete existing test suite.

## Done when
Desktop and Tests use the intended .NET 11 TFMs, packages restore with no compatibility issues, CI requests .NET 11, the whole SLNX solution builds with zero warnings or errors, and all existing tests pass.

## Research Findings

- Desktop is SDK-style WinUI with CPM and local references to the three upgraded libraries. It currently targets `net8.0-windows10.0.19041.0`; the assessment proposes `net11.0-windows10.0.22000.0` and Microsoft.WindowsAppSDK 2.7.0. NuGet confirms 2.7.0 is available.
- Tests has a direct `xunit` 2.9.3 reference and otherwise uses only common xUnit `[Fact]` and assertion APIs. The official v3 migration guide requires replacing `xunit` with `xunit.v3`, using `xunit.runner.visualstudio` 3.0.0 or later, and setting `OutputType` to `Exe`.
- NuGet's latest stable packages are `xunit.v3` 4.0.3 and `xunit.runner.visualstudio` 4.0.1; the existing seven tests use APIs covered by the v3 migration without custom extensions, async-void tests, or v2 abstraction packages.
- The existing `MemoryStream` calls use small buffers and never set capacity/length; .NET 11's maximum-capacity exception behavior is not applicable. Tests already assert the application's intentional `NotSupportedException` behavior.
- CI and README were already updated to .NET 11/SLNX in the infrastructure task. Final validation will use the full SLNX build and CLI test execution.
- xUnit v3 uses Microsoft.Testing.Platform. The .NET 11 CLI requires selecting that runner in `global.json`; the repository will pin SDK 11.0.100 with latest-feature roll-forward and set the MTP runner explicitly.
- A first .NET 11 build confirmed CommunityToolkit.Mvvm 8.4.0 cannot complete source generation for partial properties in the upgraded WinUI compilation path. Upgrade to the current compatible 8.4.2 patch rather than reverting the AOT-safe partial properties.
- Windows App SDK 2.7 requires a packaged `MsixPackage` profile in `Properties/launchSettings.json` for Visual Studio build/debug. Add the standard packaged profile and the current `Microsoft.Windows.SDK.BuildTools.WinUIAnalyzer` 0.8.0 with `PrivateAssets=all` so IDE and CI analysis remain active.

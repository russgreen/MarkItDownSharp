# 01.04 Progress Details

## Changes

- Retargeted Desktop to `net11.0-windows10.0.22000.0` and Tests to `net11.0`.
- Updated Microsoft.WindowsAppSDK from 1.6.250108002 to 2.7.0 and CommunityToolkit.Mvvm from 8.4.0 to 8.4.2.
- Added the WinUI analyzer 0.8.0 with private assets and a packaged `MsixPackage` launch profile.
- Replaced deprecated `xunit` 2.9.3 with `xunit.v3` 4.0.3, updated the Visual Studio runner to 4.0.1, set the test project `OutputType` to `Exe`, and propagated xUnit cancellation tokens.
- Added `global.json` to pin .NET SDK 11.0.100 with latest-feature roll-forward and select Microsoft.Testing.Platform.
- Made stable WinUI `x:Bind` modes explicit so analyzer diagnostics remain clean.

## Validation

- `global.json` SDK validation: passed; no changes needed.
- Full `dotnet restore MarkItDownSharp.slnx`: passed.
- Full SLNX `dotnet build`: passed with 0 warnings and 0 errors on .NET 11.
- Visual Studio workspace build: passed after adding the Windows App SDK 2.7 launch profile.
- Microsoft.Testing.Platform test run: 7 passed, 0 failed, 0 skipped.
- Final NuGet package audit: all requested final versions resolved exactly; no vulnerable packages reported.
- Repository scan: no remaining `net8.0`, `.sln` reference, `.NET 8` CI pin, old Windows App SDK reference, or xUnit v2 package reference in active configuration.
- Behavioral reviews: MemoryStream call sites remain below the new capacity boundary; NotSupportedException call sites intentionally throw application-level exceptions and do not match .NET 11 framework triggers.

## Issues resolved

- Windows App SDK 2.7 required CommunityToolkit.Mvvm 8.4.2 for C# 14 partial-property generation; the package was updated instead of reverting the AOT-safe source-generation pattern.
- xUnit v3 introduced cancellation-token analyzer diagnostics; tests now use `TestContext.Current.CancellationToken` across async I/O, monitor, wait, and conversion calls.
- .NET 11 rejected VSTest mode for the MTP test executable; `global.json` selects Microsoft.Testing.Platform.
- WinUI analyzer WUI2011 diagnostics were fixed with explicit `OneTime` bindings for stable collection/file-name values.
- Windows App SDK 2.7 IDE packaging validation required a launch profile; the standard `MsixPackage` profile was added.

## Self-review

Quality review passed. Desktop and Tests match the .NET 11 assessment targets, xUnit migration follows the official executable-project/MTP model, WinUI package settings remain intact, and the full build and test gates are warning-free.

Integrity review passed. All package versions resolved from NuGet, no package was replaced by local code, no warning/test/exception signal was suppressed, and all source/test edits are required by the framework or analyzer migrations. The unexpected `ConversionJobState.cs` split present in the working tree was excluded from the upgrade scope and will not be staged in the final commit.

# 01.01 Progress Details

## Changes

- Added root `Directory.Packages.props` with Central Package Management enabled and all six existing package versions.
- Removed package `Version` attributes from the Desktop and Tests project references.
- Converted nine CommunityToolkit observable fields in Desktop to generated partial properties and removed the nonexistent `PublishProfile` setting, eliminating all baseline warnings without suppression.
- Migrated `MarkItDownSharp.sln` to `MarkItDownSharp.slnx` and removed the old solution file.
- Updated README and Windows CI solution references to `.slnx`; updated their documented/installed .NET version to 11.
- Added baseline and post-CPM binary logs plus package-resolution snapshots and comparison report under the workflow artifacts folder.

## Validation

- .NET 11 SDK installation validation: passed.
- `dotnet clean`, restore, and SLNX build on the pre-upgrade .NET 8 TFMs: passed with 0 warnings and 0 errors.
- CLI test run: 7 passed, 0 failed, 0 skipped.
- Before/after package list comparison: all six direct package versions are unchanged and no packages were added or removed.
- Confirmed no non-workflow `.sln` reference remains; the obsolete file is staged for deletion after Visual Studio recreated it during the earlier IDE test attempt.

## Issues resolved

- MVVMTK0045: replaced annotated fields with supported partial observable properties and enabled the compiler's latest language version for Desktop.
- NETSDK1198: removed the project-level reference to a publish profile that does not exist; the existing `win-x64` runtime identifier and MSIX packaging settings remain intact.

## Self-review

Quality review passed. The CPM conversion is version-neutral, the generated partial properties preserve the existing public property contract, the publish-profile warning is resolved without changing RID/MSIX settings, and all active solution references now use SLNX.

Integrity review passed. No invented package versions, substitution stubs, unrelated package upgrades, warning suppression, weakened tests, or out-of-scope framework changes were introduced. The behavioral notices remain non-applicable, and the only solution deletion is the requested SLN-to-SLNX replacement.

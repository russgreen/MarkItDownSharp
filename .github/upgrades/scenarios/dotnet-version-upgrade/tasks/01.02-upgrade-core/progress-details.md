# 01.02 Progress Details

## Changes

- Retargeted `src/MarkItDownSharp.Core/MarkItDownSharp.Core.csproj` from `net8.0` to `net11.0`.

## Validation

- Targeted Core build: succeeded with 0 warnings and 0 errors.
- Confirmed output was produced in the `net11.0` target-framework folder.
- Behavioral advisories remain not applicable because the Core code creates and throws its own `NotSupportedException`; it does not invoke either affected .NET 11 framework API trigger.

## Self-review

Quality and integrity review passed. The only product diff is the requested `net8.0` to `net11.0` replacement in the scoped project; no package, API, warning, or test signal was silenced or substituted.

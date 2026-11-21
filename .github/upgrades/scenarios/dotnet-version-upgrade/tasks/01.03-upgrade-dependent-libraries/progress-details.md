# 01.03 Progress Details

## Changes

- Retargeted `MarkItDownSharp.Converters` from `net8.0` to `net11.0`.
- Retargeted `MarkItDownSharp.Worker` from `net8.0` to `net11.0`.

## Validation

- Converters build: succeeded with 0 warnings and 0 errors, including the `net11.0` Core dependency.
- Worker build: succeeded with 0 warnings and 0 errors, including the `net11.0` Core dependency.
- Confirmed both outputs were produced under `net11.0`.

## Self-review

Quality and integrity review passed. Both scoped project files contain only the requested TFM replacement, they continue to consume the upgraded Core project, and no package, API, warning, or test signal was suppressed or substituted.

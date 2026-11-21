# tests\MarkItDownSharp.Tests\MarkItDownSharp.Tests.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net8.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 3
- **Dependants**: 0
- **Number of Files**: 3
- **Number of Files with Incidents**: 2
- **Lines of Code**: 141
- **Estimated LOC to modify**: 4+ (at least 2.8% of the project)

## Related Projects

**Depends on (3)** — projects this one references:

- [D:\Development\Source\Repos\russgreen\MarkItDownSharp\src\MarkItDownSharp.Converters\MarkItDownSharp.Converters.csproj](../projects/MarkItDownSharp.Converters.md)
- [D:\Development\Source\Repos\russgreen\MarkItDownSharp\src\MarkItDownSharp.Core\MarkItDownSharp.Core.csproj](../projects/MarkItDownSharp.Core.md)
- [D:\Development\Source\Repos\russgreen\MarkItDownSharp\src\MarkItDownSharp.Worker\MarkItDownSharp.Worker.csproj](../projects/MarkItDownSharp.Worker.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["MarkItDownSharp.Tests.csproj"]
        MAIN["<b>📦&nbsp;MarkItDownSharp.Tests.csproj</b><br/><small>net8.0</small>"]
        click MAIN "../projects/MarkItDownSharp.Tests.md"
    end
    subgraph downstream["Dependencies (3)"]
        P2["<b>📦&nbsp;MarkItDownSharp.Converters.csproj</b><br/><small>net8.0</small>"]
        P3["<b>📦&nbsp;MarkItDownSharp.Worker.csproj</b><br/><small>net8.0</small>"]
        P1["<b>📦&nbsp;MarkItDownSharp.Core.csproj</b><br/><small>net8.0</small>"]
        click P2 "../projects/MarkItDownSharp.Converters.md"
        click P3 "../projects/MarkItDownSharp.Worker.md"
        click P1 "../projects/MarkItDownSharp.Core.md"
    end
    MAIN --> P2
    MAIN --> P3
    MAIN --> P1

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 4 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 176 |  |
| ***Total APIs Analyzed*** | ***180*** |  |

## NuGet Package Issues

| Package | Current Version | Suggested Version | Severity | Issue |
| :--- | :---: | :---: | :---: | :--- |
| xunit | 2.9.3 | — | 🔵 Optional | NuGet package is deprecated |

Every project affected by these packages, and the versions the repository settles on: [aggregate NuGet packages](../nuget/aggregate-packages.md).


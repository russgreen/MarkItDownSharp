# src\MarkItDownSharp.Core\MarkItDownSharp.Core.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net8.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 0
- **Dependants**: 4
- **Number of Files**: 5
- **Number of Files with Incidents**: 2
- **Lines of Code**: 85
- **Estimated LOC to modify**: 2+ (at least 2.4% of the project)

## Related Projects

**Depended on by (4)** — projects that reference this one:

- [D:\Development\Source\Repos\russgreen\MarkItDownSharp\src\MarkItDownSharp.Converters\MarkItDownSharp.Converters.csproj](../projects/MarkItDownSharp.Converters.md)
- [D:\Development\Source\Repos\russgreen\MarkItDownSharp\src\MarkItDownSharp.Desktop\MarkItDownSharp.Desktop.csproj](../projects/MarkItDownSharp.Desktop.md)
- [D:\Development\Source\Repos\russgreen\MarkItDownSharp\src\MarkItDownSharp.Worker\MarkItDownSharp.Worker.csproj](../projects/MarkItDownSharp.Worker.md)
- [D:\Development\Source\Repos\russgreen\MarkItDownSharp\tests\MarkItDownSharp.Tests\MarkItDownSharp.Tests.csproj](../projects/MarkItDownSharp.Tests.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph upstream["Dependants (4)"]
        P2["<b>📦&nbsp;MarkItDownSharp.Converters.csproj</b><br/><small>net8.0</small>"]
        P3["<b>📦&nbsp;MarkItDownSharp.Worker.csproj</b><br/><small>net8.0</small>"]
        P4["<b>📦&nbsp;MarkItDownSharp.Desktop.csproj</b><br/><small>net8.0-windows10.0.19041.0</small>"]
        P5["<b>📦&nbsp;MarkItDownSharp.Tests.csproj</b><br/><small>net8.0</small>"]
        click P2 "../projects/MarkItDownSharp.Converters.md"
        click P3 "../projects/MarkItDownSharp.Worker.md"
        click P4 "../projects/MarkItDownSharp.Desktop.md"
        click P5 "../projects/MarkItDownSharp.Tests.md"
    end
    subgraph current["MarkItDownSharp.Core.csproj"]
        MAIN["<b>📦&nbsp;MarkItDownSharp.Core.csproj</b><br/><small>net8.0</small>"]
        click MAIN "../projects/MarkItDownSharp.Core.md"
    end
    P2 --> MAIN
    P3 --> MAIN
    P4 --> MAIN
    P5 --> MAIN

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 2 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 171 |  |
| ***Total APIs Analyzed*** | ***173*** |  |


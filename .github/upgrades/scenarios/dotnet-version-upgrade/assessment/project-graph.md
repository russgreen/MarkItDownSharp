# Projects Relationship Graph

[← Back to the assessment index](../assessment.md)

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart LR
    P1["<b>📦&nbsp;MarkItDownSharp.Core.csproj</b><br/><small>net8.0</small>"]
    P2["<b>📦&nbsp;MarkItDownSharp.Converters.csproj</b><br/><small>net8.0</small>"]
    P3["<b>📦&nbsp;MarkItDownSharp.Worker.csproj</b><br/><small>net8.0</small>"]
    P4["<b>📦&nbsp;MarkItDownSharp.Desktop.csproj</b><br/><small>net8.0-windows10.0.19041.0</small>"]
    P5["<b>📦&nbsp;MarkItDownSharp.Tests.csproj</b><br/><small>net8.0</small>"]
    P2 --> P1
    P3 --> P1
    P4 --> P2
    P4 --> P3
    P4 --> P1
    P5 --> P2
    P5 --> P3
    P5 --> P1
    click P1 "projects/MarkItDownSharp.Core.md"
    click P2 "projects/MarkItDownSharp.Converters.md"
    click P3 "projects/MarkItDownSharp.Worker.md"
    click P4 "projects/MarkItDownSharp.Desktop.md"
    click P5 "projects/MarkItDownSharp.Tests.md"

```


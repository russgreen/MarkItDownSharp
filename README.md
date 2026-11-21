# MarkItDownSharp

MarkItDownSharp is a Windows-only C# implementation of the [MarkItDown](https://github.com/microsoft/markitdown) concept. The WinUI 3 desktop application monitors a source folder and writes Markdown into a separate output folder.

## MVP capabilities

- Choose a source folder and output folder.
- Start or stop folder monitoring.
- Monitor recursively and optionally process files that already exist.
- Convert plain text, Markdown, log, CSV, TSV, HTML, JSON, XML, and YAML files.
- Debounce file events and wait for files to finish being written.
- Preserve source subdirectories in the output tree.
- Atomically write `.md` files.
- Overwrite, skip, or version existing output files.
- Reconcile missed file events periodically.
- Show queued, waiting, processing, completed, skipped, and failed jobs.
- Restore the previous configuration from local application data.

## Requirements

- 64-bit Windows 10 version 1809 or later
- .NET 11 SDK
- Visual Studio 2026 with the **Windows application development** workload

## Build and run

Open `MarkItDownSharp.slnx` in Visual Studio on Windows, select `x64`, and run `MarkItDownSharp.Desktop`.

Or build from a Windows terminal:

```powershell
dotnet restore MarkItDownSharp.slnx
dotnet build src\MarkItDownSharp.Desktop\MarkItDownSharp.Desktop.csproj -c Debug -p:Platform=x64
```

The default package is an unsigned MSIX for local development. Production publishing requires a package identity and signing certificate.

## Tests

The conversion and folder-monitoring tests run on any platform:

```bash
dotnet test tests/MarkItDownSharp.Tests/MarkItDownSharp.Tests.csproj
```

The WinUI XAML compiler is Windows-only; the Windows CI workflow builds the desktop application on `windows-latest`.

## Structure

- `MarkItDownSharp.Core` — conversion contracts and registry
- `MarkItDownSharp.Converters` — native C# format converters
- `MarkItDownSharp.Worker` — watcher, queue, file stabilization, and output writing
- `MarkItDownSharp.Desktop` — WinUI 3 application and persisted settings
- `MarkItDownSharp.Tests` — conversion and watcher integration tests

Office, PDF, EPUB, image, OCR, audio, and cloud conversions are intentionally outside this initial MVP.
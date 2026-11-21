# MarkItDownSharp

MarkItDownSharp is a Windows-only C# implementation of the [MarkItDown](https://github.com/microsoft/markitdown) concept. The WinUI 3 desktop application monitors a source folder and writes Markdown into a separate output folder.

## Capabilities

**Formats** (converted locally unless noted)

- Text, Markdown, logs, CSV/TSV, HTML, JSON, XML, YAML (encoding is detected and line endings normalised)
- Word (DOCX), Excel (XLSX, XLS), PowerPoint (PPTX), PDF, EPUB
- ZIP archives, including nested archives (depth, entry-count and size limits apply)
- RSS and Atom feeds, Jupyter notebooks (IPYNB), Outlook messages (MSG)
- Image metadata and audio tags
- Optional: OCR of images in Office files, PDFs and image files with the Windows OCR engine
- Optional, off by default and sending content to the endpoint you configure: image descriptions and speech transcription through any OpenAI-compatible endpoint (local or hosted), Azure AI Document Intelligence, Azure AI Content Understanding

**Monitoring**

- Recursive monitoring, optional processing of existing files, nested output folders (output is never re-read)
- Configurable debounce and rescan interval; temporary and lock files are ignored
- Files are converted only once no writer holds them and their size has stopped changing
- Bounded retries for transient file-lock errors; queue size limit with wait or drop behaviour
- Cancel one job or all, retry failed jobs, pause and resume, graceful stop that finishes queued files
- Deleted sources are detected (optionally removing their output); renames are followed
- Overwrite, skip or version existing output; output is written atomically

**Desktop app**

- Folder validation with clear permission and path errors
- Job list with a detail pane, Open source / Open output / Show in folder, Retry, Cancel, Clear history
- Job history is saved and restored after a restart
- Toast notifications (never, failures only, every conversion), light/dark/system theme
- Start with Windows, start monitoring on launch, keep running in the notification area
- Converters page to switch converters on or off, diagnostics with versions, local crash reports

**Extensibility**

- Converters declare a name, description, extensions and priority; the lowest priority number wins
- Plugin converters are loaded from the plugins folder only when explicitly trusted by SHA-256 hash or by an Authenticode signer thumbprint

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

The default package is an unsigned MSIX for local development. See [`docs/RELEASING.md`](docs/RELEASING.md) for signed packages, install and upgrade tests, and releases.

## Tests

The conversion, monitoring, view-model, packaging and XAML-lint tests run on any platform with the .NET 11 SDK:

```powershell
dotnet test tests/MarkItDownSharp.Tests/MarkItDownSharp.Tests.csproj
```

The WinUI XAML compiler and the MSIX install/upgrade tests are Windows-only; the Windows CI workflow runs them on `windows-latest`.

## Structure

- `MarkItDownSharp.Core` — conversion contracts, converter registry, plugin discovery and trust policy
- `MarkItDownSharp.Converters` — native C# format converters and optional integrations
- `MarkItDownSharp.Worker` — watcher, queue, file stabilisation, output writing, folder validation
- `MarkItDownSharp.Presentation` — view models, settings and history storage, crash reporting (no WinUI dependency, so it is unit-tested)
- `MarkItDownSharp.Desktop` — WinUI 3 shell: XAML, tray icon, notifications, startup task, Windows OCR
- `MarkItDownSharp.Tests` — unit, integration and static checks
- `build/` — packaging, signing, install and upgrade scripts

Implementation planning and remaining work are documented in [`plan.md`](plan.md) and [`backlog.md`](backlog.md).

## Privacy

Everything is converted on your computer unless you configure a network converter on the Settings page. Diagnostics and crash reports contain versions and call stacks only, never file names, paths or document content, and nothing is uploaded automatically.

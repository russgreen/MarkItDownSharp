# MarkItDownSharp Implementation Plan

## Objective

Create a Windows-only WinUI 3 desktop application that monitors a source folder and converts newly added or changed supported files to Markdown in a separate output folder.

## MVP

The MVP is complete when a user can:

1. Select a monitored folder and an output folder.
2. Start and stop monitoring.
3. Choose recursive monitoring and existing-file processing.
4. Add or replace a supported file.
5. See queued, processing, completed, skipped, or failed status.
6. Find the generated `.md` file in the matching output subdirectory.

## Architecture

- `MarkItDownSharp.Core`
  - Conversion interfaces and registry
  - Stream metadata
  - Conversion results
- `MarkItDownSharp.Converters`
  - Plain text and Markdown
  - CSV and TSV
  - HTML
  - JSON, XML, and YAML
- `MarkItDownSharp.Worker`
  - FileSystemWatcher event ingestion
  - Debouncing and file-stability checks
  - Bounded background queue
  - Periodic missed-event reconciliation
  - Relative output paths and atomic writes
  - Overwrite, skip, and version collision policies
- `MarkItDownSharp.Desktop`
  - WinUI 3 and MVVM UI
  - Folder pickers
  - Configuration persistence
  - Live conversion job status

## Delivery phases

### 1. Foundation

- Create the solution and project boundaries.
- Implement converter metadata and a priority-ordered converter registry.
- Add initial conversion tests.

### 2. Monitoring pipeline

- Debounce duplicate watcher events.
- Wait for files to become readable and stable.
- Preserve subdirectories in the output.
- Write through temporary files and atomically replace destinations.
- Dedupe files by size and last-write fingerprint.
- Reconcile missed events during a running session.

### 3. WinUI MVP

- Add source/output folder selection.
- Persist application settings.
- Add Start/Stop controls and monitoring options.
- Show conversion status and errors.
- Package the application as MSIX.

### 4. Office and document formats

- Add DOCX, XLSX, PPTX, PDF, EPUB, and ZIP conversion.
- Establish format-specific golden fixtures and structural assertions.

### 5. Optional integrations

- Add image metadata and captioning.
- Add OCR and audio transcription.
- Add Azure Document Intelligence and Content Understanding.
- Add signed plugin discovery and third-party converter registration.

## Validation

- Conversion and folder-monitor tests run on all .NET-supported development platforms.
- WinUI XAML and MSIX builds run on `windows-latest` GitHub Actions.
- Tests cover duplicate events, partial writes, nested folders, output collisions, unsupported files, output nesting, and restart behavior.
- Windows testing must cover folder permissions, FileSystemWatcher behavior, settings persistence, and clean MSIX installation/uninstallation.

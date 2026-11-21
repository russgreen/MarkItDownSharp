# MarkItDownSharp Backlog

## High priority

- [ ] Fix and validate MSIX assets and package identity on Windows.
- [ ] Run and verify the WinUI application build on `windows-latest`.
- [ ] Add DOCX conversion.
- [ ] Add XLSX and XLS conversion.
- [ ] Add PPTX conversion.
- [ ] Add PDF conversion.
- [ ] Add EPUB conversion.
- [ ] Add ZIP/archive recursion.
- [ ] Add a settings validation surface with clear folder and permission errors.
- [ ] Add retry controls for failed conversion jobs.
- [ ] Persist failed, successful, and skipped jobs across application restarts.
- [ ] Add end-to-end WinUI smoke tests.

## Reliable monitoring

- [ ] Add bounded retries for transient file locks and I/O failures.
- [ ] Detect stable files without depending on first-write metadata.
- [ ] Add rename/delete handling and temporary-file filters.
- [ ] Support configurable debounce and reconciliation intervals.
- [ ] Prevent output files from being reprocessed when nested under a monitored input folder.
- [ ] Add maximum queue size and backpressure configuration.
- [ ] Add cancellation for individual jobs and the entire monitor.
- [ ] Add shutdown flushing for queued jobs.
- [ ] Improve reconciliation for deleted source files and stale output records.

## Desktop experience

- [ ] Add application and per-job notification behavior.
- [ ] Add a detail pane for conversion errors and output paths.
- [ ] Add Open Source and Open Output actions.
- [ ] Add a Clear History action.
- [ ] Add pause/resume monitoring.
- [ ] Add system tray and startup-with-Windows behavior.
- [ ] Add dark, light, and high-contrast theme validation.
- [ ] Add accessibility names and keyboard navigation checks.
- [ ] Add first-run guidance and a supported-format summary.

## Converter maturity

- [ ] Add plain-text encoding detection and newline normalization.
- [ ] Improve HTML conversion using a structured HTML-to-Markdown library.
- [ ] Improve JSON, YAML, and XML formatting.
- [ ] Add RSS and Atom feed conversion.
- [ ] Add IPYNB conversion.
- [ ] Add Outlook MSG conversion.
- [ ] Add image metadata and optional local/hosted image descriptions.
- [ ] Add audio metadata and optional speech transcription.
- [ ] Add OCR extraction for Office and PDF documents.
- [ ] Add Azure Document Intelligence conversion.
- [ ] Add Azure Content Understanding conversion.

## Extensibility

- [ ] Add custom converter discovery and registration.
- [ ] Define converter capability metadata and priority declarations.
- [ ] Add disabled/enabled converter configuration.
- [ ] Add a converter diagnostics page.
- [ ] Add optional third-party package/plugin loading.
- [ ] Add plugin trust and signature policy.

## Packaging and release

- [ ] Replace placeholder package icons with final artwork.
- [ ] Configure a stable MSIX publisher identity.
- [ ] Add signing for release builds.
- [ ] Add MSIX upgrade tests.
- [ ] Add installer/uninstaller smoke tests.
- [ ] Add versioned application and converter diagnostics.
- [ ] Publish a signed Windows release artifact.
- [ ] Add crash reporting without collecting document contents.

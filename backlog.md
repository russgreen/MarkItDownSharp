# MarkItDownSharp Backlog

`[x]` means implemented and checked by automated tests or by a local Windows build. A line under an item says what
was not verified. `[ ]` items are open; the note says what they are waiting for.

## High priority

- [x] Fix and validate MSIX assets and package identity on Windows.
  - An unsigned MSIX builds locally with MakeAppx; `PackagingTests` check the manifest, identity rules and every asset size. The publisher is still the placeholder `CN=MarkItDownSharp` (see Packaging and release).
- [ ] Run and verify the WinUI application build on `windows-latest`.
  - The workflow now builds, tests and runs the MSIX jobs, and the same build passes locally on Windows. It has not run on GitHub yet; push the branch to see it.
- [x] Add DOCX conversion.
- [x] Add XLSX conversion.
- [ ] Add XLS conversion.
  - Implemented with the same converter as XLSX (ExcelDataReader reads both), but there is no `.xls` fixture, so nothing exercises the legacy format. Add a small real `.xls` file to the tests.
- [x] Add PPTX conversion.
- [x] Add PDF conversion.
- [x] Add EPUB conversion.
- [x] Add ZIP/archive recursion.
- [x] Add a settings validation surface with clear folder and permission errors.
- [x] Add retry controls for failed conversion jobs.
- [x] Persist failed, successful, and skipped jobs across application restarts.
- [ ] Add end-to-end WinUI smoke tests.
  - View-model level end-to-end tests run in `dotnet test`. `build/Test-MsixInstall.ps1` installs the package, runs the real app, checks the converted file and the UI Automation tree, but it has only been parse-checked, not run (it installs a package and trusts a certificate).

## Reliable monitoring

- [x] Add bounded retries for transient file locks and I/O failures.
- [x] Detect stable files without depending on first-write metadata.
- [x] Add rename/delete handling and temporary-file filters.
- [x] Support configurable debounce and reconciliation intervals.
- [x] Prevent output files from being reprocessed when nested under a monitored input folder.
- [x] Add maximum queue size and backpressure configuration.
- [x] Add cancellation for individual jobs and the entire monitor.
- [x] Add shutdown flushing for queued jobs.
- [x] Improve reconciliation for deleted source files and stale output records.

## Desktop experience

- [x] Add application and per-job notification behavior.
  - Behaviour (modes, throttling) is tested. The Windows toast itself has not been seen on screen.
- [x] Add a detail pane for conversion errors and output paths.
- [x] Add Open Source and Open Output actions.
- [x] Add a Clear History action.
- [x] Add pause/resume monitoring.
- [ ] Add system tray and startup-with-Windows behavior.
  - Implemented (Shell_NotifyIcon wrapper, startup task in the manifest) and it compiles, but neither has been run. The startup task only works from the installed MSIX.
- [x] Add dark, light, and high-contrast theme validation.
  - `XamlLintTests` reject hard-coded colours and non-theme brushes, and a theme selector is available. Nobody has looked at the three themes on screen.
- [x] Add accessibility names and keyboard navigation checks.
  - Lint tests cover names, headings, tab stops and shortcuts. No Narrator pass yet.
- [x] Add first-run guidance and a supported-format summary.

## Converter maturity

- [x] Add plain-text encoding detection and newline normalization.
- [x] Improve HTML conversion using a structured HTML-to-Markdown library.
- [x] Improve JSON, YAML, and XML formatting.
- [x] Add RSS and Atom feed conversion.
- [x] Add IPYNB conversion.
- [ ] Add Outlook MSG conversion.
  - Implemented with MsgReader, but there is no real `.msg` file to test it with.
- [x] Add image metadata and optional local/hosted image descriptions.
  - Descriptions are tested against a fake HTTP endpoint, not a live model.
- [x] Add audio metadata and optional speech transcription.
  - Transcription is tested against a fake HTTP endpoint, not a live service.
- [x] Add OCR extraction for Office and PDF documents.
  - Extraction is tested with a fake OCR engine. The Windows OCR engine (`WindowsOcrEngine`) compiles but has not been run.
- [ ] Add Azure Document Intelligence conversion.
  - Implemented against the documented REST flow and tested with a fake service. Needs a live Azure resource to confirm.
- [ ] Add Azure Content Understanding conversion.
  - Same as above; this API is in preview and the request shape should be re-checked against a live resource.

## Extensibility

- [x] Add custom converter discovery and registration.
- [x] Define converter capability metadata and priority declarations.
- [x] Add disabled/enabled converter configuration.
- [x] Add a converter diagnostics page.
- [x] Add optional third-party package/plugin loading.
- [x] Add plugin trust and signature policy.
  - Plugins load only when pinned by SHA-256 hash or signed by a trusted Authenticode signer (WinVerifyTrust). Plugin dependencies in the same folder are not individually verified.

## Packaging and release

- [ ] Replace placeholder package icons with final artwork.
  - Needs the artwork. `docs/RELEASING.md` lists the sizes the tests expect.
- [ ] Configure a stable MSIX publisher identity.
  - Needs a decision and a certificate. `Build-Msix.ps1` takes the publisher from the signing certificate so builds cannot disagree with it.
- [ ] Add signing for release builds.
  - `Build-Msix.ps1` signs with signtool and `release.yml` is ready, but it needs the `SIGNING_CERT_BASE64` and `SIGNING_CERT_PASSWORD` secrets and has not run.
- [ ] Add MSIX upgrade tests.
  - `build/Test-MsixUpgrade.ps1` and the `msix` CI job exist; not run yet.
- [ ] Add installer/uninstaller smoke tests.
  - `build/Test-MsixInstall.ps1` and the `msix` CI job exist; not run yet.
- [x] Add versioned application and converter diagnostics.
- [ ] Publish a signed Windows release artifact.
  - Needs the signing certificate and a tag push.
- [x] Add crash reporting without collecting document contents.
  - Reports are written locally (exception type and call stack only) and never uploaded. Remote reporting is a separate decision.

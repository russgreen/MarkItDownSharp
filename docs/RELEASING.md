# Packaging and releasing

## Version

The version lives in `Directory.Build.props` (`0.1.0`). `Package.appxmanifest` carries the same number with a
trailing `.0` (the Microsoft Store reserves the last part). `PackagingTests` fails if they drift apart.
`Build-Msix.ps1` stamps the manifest for the build and restores it afterwards.

## Publisher identity

An MSIX publisher must equal the subject of the certificate that signs it, and it must never change between
releases or Windows treats the package as a different app and refuses to upgrade it.

1. Decide the certificate subject once (for example `CN=Your Company, O=Your Company, C=GB`).
2. Obtain a code-signing certificate with that subject (Azure Trusted Signing, a commercial CA, or an
   organisation CA).
3. Put the same subject in `Package.appxmanifest` (`Identity/@Publisher`).

`Build-Msix.ps1` reads the subject from the PFX when you pass `-PfxPath`, so a mismatch is impossible in release
builds. The committed `CN=MarkItDownSharp` is a placeholder for local, unsigned builds.

## Local test packages

```powershell
./build/New-TestCertificate.ps1 -PfxPath artifacts/test.pfx -Password 'test-only'
./build/Build-Msix.ps1 -Version 0.1.0.0 -PfxPath artifacts/test.pfx -PfxPassword 'test-only'
```

Installing a package needs the certificate to be trusted (`New-TestCertificate.ps1 -Trust`, elevated). This
changes the machine certificate store, so prefer a disposable VM or the CI runner.

## Install, upgrade and uninstall tests

```powershell
./build/Test-MsixInstall.ps1 -MsixPath artifacts/MarkItDownSharp_0.1.1.0_x64.msix -CertificatePath artifacts/test.cer
./build/Test-MsixUpgrade.ps1 -OldMsixPath artifacts/MarkItDownSharp_0.1.0.0_x64.msix `
    -NewMsixPath artifacts/MarkItDownSharp_0.1.1.0_x64.msix -CertificatePath artifacts/test.cer
```

The install test installs the package, seeds a settings file and a sample document, starts the real app, waits
for the converted Markdown, checks the window's controls through UI Automation, uninstalls and verifies that
no application data is left. The upgrade test installs an older version, converts a file, upgrades in place and
checks that settings and job history survived. The `msix` job in `.github/workflows/windows-build.yml` runs both.

## Signed release

Pushing a tag such as `v0.2.0` runs `.github/workflows/release.yml`, which builds, signs and publishes the MSIX
as a GitHub release asset. Configure these repository secrets first:

- `SIGNING_CERT_BASE64` — the PFX, base64 encoded (`[Convert]::ToBase64String([IO.File]::ReadAllBytes('release.pfx'))`)
- `SIGNING_CERT_PASSWORD` — its password

## Final artwork

The files in `src/MarkItDownSharp.Desktop/Assets` are placeholders. Replace them with final artwork at the sizes
checked by `PackagingTests` (44x44, 50x50, 71x71, 150x150, 620x300 PNG, and `AppIcon.ico`, which the tray icon and
the executable use). Add scale variants (`.scale-200`) when the artwork exists.

## Crash reports

Crash reports are written to `%LOCALAPPDATA%\MarkItDownSharp\crash-reports` (inside the package's local cache
when installed as MSIX). They contain the exception type and call stack only. Nothing is uploaded; users can
attach a report to an issue. Adding an upload endpoint would need a privacy review and explicit user consent.

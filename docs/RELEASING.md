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

## MSI installer (WiX) and the Fallout build

Besides the MSIX, the app ships as a classic per-machine MSI built with WiX 7 (`installer/MarkItDownSharp.Setup.wixproj`).
The MSI installs the **unpackaged**, self-contained build of the desktop app (no package identity, Windows App SDK
bundled). Because there is no package identity, "Start with Windows" uses a per-user `Run` registry entry instead of
the MSIX startup task, and the app is started with `--startup` from it.

The Fallout build project (`build/_build.csproj`) runs the whole pipeline:

```powershell
./build.ps1            # Clean -> Compile -> Test -> Publish   (Sign and Installer only run on main)
./build.ps1 Publish    # just the unpackaged publish into output/MarkItDownSharp
```

On the `main` branch the same command also signs the app binaries and the MSI (it uses the code-signing certificate
named in `build/Build.Sign.cs` from the local certificate store) and writes
`installer/MarkItDownSharp-SetupFiles/MarkItDownSharp.<version>.msi`. To build an unsigned MSI on any branch, run
`./build.ps1 Publish` and then:

```powershell
dotnet build installer/MarkItDownSharp.Setup.wixproj -c Release
```

Notes:

- The version comes from `<Version>` in the root `Directory.Build.props`; the Fallout target passes it to WiX as `ProductVersion`.
- The `UpgradeCode` in `installer/Package.wxs` must never change: it is how a new MSI replaces an installed one.
- The wixproj sets `AcceptEula` for the WiX 7 Open Source Maintenance Fee EULA. That is a legal acceptance, see <https://wixtoolset.org/osmf/>.
- The Fallout build project targets `net10.0` because Fallout 11 fails its start-up check on the .NET 11 runtime. Install the .NET 10 runtime as well as the .NET 11 SDK to run it.
- The Setup UI (`installer/UI.wxs`) is the shared template: change the template, not the copy. The banner art was made with the Russ Green art kit.

## Final artwork

The files in `src/MarkItDownSharp.Desktop/Assets` are placeholders. Replace them with final artwork at the sizes
checked by `PackagingTests` (44x44, 50x50, 71x71, 150x150, 620x300 PNG, and `AppIcon.ico`, which the tray icon and
the executable use). Add scale variants (`.scale-200`) when the artwork exists.

## Crash reports

Crash reports are written to `%LOCALAPPDATA%\MarkItDownSharp\crash-reports` (inside the package's local cache
when installed as MSIX). They contain the exception type and call stack only. Nothing is uploaded; users can
attach a report to an issue. Adding an upload endpoint would need a privacy review and explicit user consent.

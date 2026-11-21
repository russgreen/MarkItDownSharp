<#
.SYNOPSIS
    Installs the signed MSIX, runs the real application, checks it converts a file and that its window is
    reachable through UI Automation, then uninstalls it and checks nothing is left behind.

.NOTES
    Intended for CI runners: it trusts the test certificate machine-wide and installs a package.
    Requires an elevated PowerShell 7 session.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsixPath,
    [Parameter(Mandatory)][string]$CertificatePath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageTestHelpers.ps1')

$work = Join-Path ([System.IO.Path]::GetTempPath()) "mids-install-$([guid]::NewGuid().ToString('N'))"
$inputDir = Join-Path $work 'input'
$outputDir = Join-Path $work 'output'
New-Item -ItemType Directory -Force -Path $inputDir | Out-Null
$package = $null

try {
    Install-TestCertificate $CertificatePath
    Add-AppxPackage -Path $MsixPath
    $package = Get-AppxPackage -Name 'MarkItDownSharp'
    if (-not $package) { throw 'The package is not installed after Add-AppxPackage.' }
    Write-Host "Installed $($package.PackageFullName)"

    Set-Content -LiteralPath (Join-Path $inputDir 'smoke.txt') -Value 'installer smoke test' -Encoding UTF8
    Write-TestSettings -Package $package -InputDirectory $inputDir -OutputDirectory $outputDir

    $process = Start-PackagedApp $package
    Write-Host "Application started (PID $($process.Id))."

    # 1. The monitor starts by itself and converts the seeded file.
    Wait-ForFile (Join-Path $outputDir 'smoke.md')
    $markdown = (Get-Content -LiteralPath (Join-Path $outputDir 'smoke.md') -Raw).Trim()
    if ($markdown -ne 'installer smoke test') { throw "Unexpected conversion output: '$markdown'" }
    Write-Host 'Conversion produced the expected Markdown.'

    # 2. The window exposes its controls to UI Automation.
    foreach ($name in 'Start monitoring', 'Pause or resume monitoring', 'Stop monitoring', 'Conversion jobs', 'Settings', 'Converters', 'About and diagnostics') {
        $null = Find-UiElement -Process $process -Name $name
    }
    Write-Host 'Main window controls are reachable through UI Automation.'

    # 3. The app survives for a while without crashing.
    Start-Sleep -Seconds 5
    if ($process.HasExited) { throw 'The application exited unexpectedly.' }

    Stop-PackagedApp
}
finally {
    Stop-PackagedApp
    if ($package) {
        Remove-AppxPackage -Package $package.PackageFullName
        if (Get-AppxPackage -Name 'MarkItDownSharp') { throw 'The package is still installed after Remove-AppxPackage.' }
        if (Test-Path -LiteralPath (Get-AppDataFolder $package)) { throw 'Application data was left behind after uninstall.' }
        Write-Host 'Uninstalled cleanly.'
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

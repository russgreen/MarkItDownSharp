<#
.SYNOPSIS
    Installs an older MSIX, creates user data, upgrades in place to a newer MSIX and checks that the package
    version changed while settings and conversion history survived, then uninstalls.

.NOTES
    Intended for CI runners. Requires an elevated PowerShell 7 session and two packages signed by the same
    certificate (see Build-Msix.ps1).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OldMsixPath,
    [Parameter(Mandatory)][string]$NewMsixPath,
    [Parameter(Mandatory)][string]$CertificatePath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageTestHelpers.ps1')

$work = Join-Path ([System.IO.Path]::GetTempPath()) "mids-upgrade-$([guid]::NewGuid().ToString('N'))"
$inputDir = Join-Path $work 'input'
$outputDir = Join-Path $work 'output'
New-Item -ItemType Directory -Force -Path $inputDir | Out-Null
$package = $null

try {
    Install-TestCertificate $CertificatePath

    Add-AppxPackage -Path $OldMsixPath
    $old = Get-AppxPackage -Name 'MarkItDownSharp'
    Write-Host "Installed old version $($old.Version)"

    Set-Content -LiteralPath (Join-Path $inputDir 'before.txt') -Value 'converted by the old version' -Encoding UTF8
    Write-TestSettings -Package $old -InputDirectory $inputDir -OutputDirectory $outputDir
    $process = Start-PackagedApp $old
    Wait-ForFile (Join-Path $outputDir 'before.md')
    Stop-PackagedApp

    $data = Get-AppDataFolder $old
    Wait-ForFile (Join-Path $data 'history.json') 30
    $settingsBefore = Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw

    # In-place upgrade: same identity, higher version.
    Add-AppxPackage -Path $NewMsixPath
    $package = Get-AppxPackage -Name 'MarkItDownSharp'
    if ([version]$package.Version -le [version]$old.Version) {
        throw "Upgrade did not raise the version ($($old.Version) -> $($package.Version))."
    }
    Write-Host "Upgraded $($old.Version) -> $($package.Version)"

    $settingsAfter = Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw
    if ($settingsAfter -ne $settingsBefore) { throw 'Settings changed during the upgrade.' }
    $history = Get-Content -LiteralPath (Join-Path $data 'history.json') -Raw | ConvertFrom-Json
    if (-not ($history | Where-Object { $_.SourcePath -like '*before.txt' })) { throw 'Conversion history was lost in the upgrade.' }

    # The upgraded app starts and still converts new files.
    Set-Content -LiteralPath (Join-Path $inputDir 'after.txt') -Value 'converted by the new version' -Encoding UTF8
    $process = Start-PackagedApp $package
    Wait-ForFile (Join-Path $outputDir 'after.md')
    Stop-PackagedApp
    Write-Host 'Upgrade preserved data and the new version works.'
}
finally {
    Stop-PackagedApp
    foreach ($installed in Get-AppxPackage -Name 'MarkItDownSharp') {
        Remove-AppxPackage -Package $installed.PackageFullName
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

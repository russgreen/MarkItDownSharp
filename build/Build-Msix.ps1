<#
.SYNOPSIS
    Builds the MarkItDownSharp MSIX package and optionally signs it.

.DESCRIPTION
    - Stamps -Version (four parts, last part 0) and the publisher into Package.appxmanifest for the build and
      restores the file afterwards.
    - Builds the unsigned package with MSBuild, then signs it with signtool when -PfxPath is given.
    - The publisher must equal the subject of the signing certificate. It is read from the PFX unless -Publisher
      is passed explicitly.

.EXAMPLE
    ./build/Build-Msix.ps1 -Version 0.1.0.0 -PfxPath ./artifacts/test.pfx -PfxPassword 'test-only'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.0$')][string]$Version,
    [string]$PfxPath,
    [string]$PfxPassword = $env:SIGNING_CERT_PASSWORD,
    [string]$Publisher,
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $repo 'src\MarkItDownSharp.Desktop\MarkItDownSharp.Desktop.csproj'
$manifest = Join-Path $repo 'src\MarkItDownSharp.Desktop\Package.appxmanifest'

function Find-SignTool {
    $candidates = @(
        Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue
        Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\signtool.exe') -ErrorAction SilentlyContinue
    ) | Sort-Object FullName -Descending
    if (-not $candidates) { throw 'signtool.exe was not found. Install the Windows SDK.' }
    return $candidates[0].FullName
}

if ($PfxPath -and -not $Publisher) {
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path $PfxPath), $PfxPassword, $flags)
    $Publisher = $certificate.Subject
}

$original = Get-Content -LiteralPath $manifest -Raw
try {
    $stamped = $original -replace '(<Identity\b[^>]*\bVersion=")[^"]*(")', "`${1}$Version`${2}"
    if ($Publisher) {
        $stamped = $stamped -replace '(<Identity\b[^>]*\bPublisher=")[^"]*(")', "`${1}$Publisher`${2}"
    }
    [System.IO.File]::WriteAllText($manifest, $stamped, [System.Text.UTF8Encoding]::new($false))

    dotnet build $project -c $Configuration -p:Platform=x64 `
        -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false `
        -p:AppxBundle=Never -p:UapAppxPackageBuildMode=SideloadOnly `
        -p:Version=$($Version.Substring(0, $Version.LastIndexOf('.')))
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    $built = Get-ChildItem (Join-Path $repo "src\MarkItDownSharp.Desktop\AppPackages\*_${Version}_x64*\*.msix") |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $built) { throw "No .msix for version $Version was produced." }

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $destination = Join-Path (Resolve-Path $OutputDirectory) "MarkItDownSharp_${Version}_x64.msix"
    Copy-Item -LiteralPath $built.FullName -Destination $destination -Force

    if ($PfxPath) {
        & (Find-SignTool) sign /fd SHA256 /f $PfxPath /p $PfxPassword /td SHA256 $destination
        if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }
        Write-Host "Signed $destination as $Publisher"
    }
    else {
        Write-Warning 'No certificate given: the package is unsigned and cannot be installed.'
    }

    Write-Output $destination
}
finally {
    [System.IO.File]::WriteAllText($manifest, $original, [System.Text.UTF8Encoding]::new($false))
}

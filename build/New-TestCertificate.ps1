<#
.SYNOPSIS
    Creates a self-signed code-signing certificate for building and testing MSIX packages.

.DESCRIPTION
    The certificate is for CI and local testing only. Release builds use the organisation's real certificate.
    The PFX is written to -PfxPath and the public certificate to the same path with a .cer extension.
    Pass -Trust to also trust the public certificate for package installation (needs an elevated shell and
    changes the machine's certificate store, so it is opt-in).

.EXAMPLE
    ./build/New-TestCertificate.ps1 -PfxPath ./artifacts/test.pfx -Password 'test-only'
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=MarkItDownSharp Test',
    [Parameter(Mandatory)][string]$PfxPath,
    [Parameter(Mandatory)][string]$Password,
    [switch]$Trust
)

$ErrorActionPreference = 'Stop'

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -FriendlyName 'MarkItDownSharp test signing' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

$folder = Split-Path -Parent $PfxPath
if ($folder) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }
$secure = ConvertTo-SecureString -String $Password -AsPlainText -Force
Export-PfxCertificate -Cert $certificate -FilePath $PfxPath -Password $secure | Out-Null
$cerPath = [System.IO.Path]::ChangeExtension($PfxPath, '.cer')
Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null

if ($Trust) {
    Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    Write-Host "Trusted $($certificate.Thumbprint) in LocalMachine\TrustedPeople."
}

# The test certificate does not belong in the user's personal store after export.
Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force

Write-Host "PFX:         $PfxPath"
Write-Host "Certificate: $cerPath"
Write-Host "Subject:     $($certificate.Subject)"
Write-Host "Thumbprint:  $($certificate.Thumbprint)"

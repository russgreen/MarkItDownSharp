# Shared helpers for the MSIX install and upgrade tests. Dot-source this file.

function Install-TestCertificate {
    param([Parameter(Mandatory)][string]$CertificatePath)
    # Sideloading requires the signer to be trusted; this changes the machine store, so these tests are for CI runners.
    Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
}

function Get-AppDataFolder {
    param([Parameter(Mandatory)]$Package)
    # Packaged full-trust apps have writes under %LOCALAPPDATA% redirected into the package's LocalCache.
    Join-Path $env:LOCALAPPDATA "Packages\$($Package.PackageFamilyName)\LocalCache\Local\MarkItDownSharp"
}

function Write-TestSettings {
    param(
        [Parameter(Mandatory)]$Package,
        [Parameter(Mandatory)][string]$InputDirectory,
        [Parameter(Mandatory)][string]$OutputDirectory
    )
    $folder = Get-AppDataFolder $Package
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $settings = [ordered]@{
        SourceDirectory         = $InputDirectory
        OutputDirectory         = $OutputDirectory
        IncludeSubdirectories   = $true
        ProcessExistingFiles    = $true
        StartMonitoringOnLaunch = $true
        HasSeenWelcome          = $true
    }
    $settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'settings.json') -Encoding UTF8
}

function Start-PackagedApp {
    param([Parameter(Mandatory)]$Package)
    Start-Process 'explorer.exe' "shell:AppsFolder\$($Package.PackageFamilyName)!App"
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
        $process = Get-Process -Name 'MarkItDownSharp.Desktop' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    } until ($process -or (Get-Date) -gt $deadline)
    if (-not $process) { throw 'The application window did not appear within 60 seconds.' }
    return $process
}

function Wait-ForFile {
    param([Parameter(Mandatory)][string]$Path, [int]$TimeoutSeconds = 90)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $Path)) {
        if ((Get-Date) -gt $deadline) { throw "Timed out waiting for $Path" }
        Start-Sleep -Milliseconds 500
    }
}

function Find-UiElement {
    param([Parameter(Mandatory)]$Process, [Parameter(Mandatory)][string]$Name)
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 500
    } until ((Get-Date) -gt $deadline)
    throw "UI element '$Name' was not found in the application window."
}

function Stop-PackagedApp {
    Get-Process -Name 'MarkItDownSharp.Desktop' -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not $_.CloseMainWindow()) { $_.Kill() }
        if (-not $_.WaitForExit(15000)) { $_.Kill() }
    }
}

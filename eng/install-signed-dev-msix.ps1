# EventLogExpert dev install — run from an ELEVATED PowerShell.
# Trusts the dev signing cert in LocalMachine\Root and installs the signed MSIX so packaged
# COM + Explorer context menu extensions register with the shell. Loose-file
# Add-AppxPackage -Register leaves SignatureKind=None which silently drops those extensions.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$PfxPath = "$env:TEMP\eventlogexpert-dev.pfx",
    [string]$Configuration = 'Debug',
    [string]$PfxPassword = 'elx-dev-cert'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must run from an ELEVATED PowerShell (right-click PowerShell -> Run as administrator)."
    exit 1
}

$msix = Join-Path $RepoRoot "src\EventLogExpert\bin\$Configuration\net10.0-windows10.0.19041.0\win-x64\AppPackages\EventLogExpert_0.9.0.0_${Configuration}_Test\EventLogExpert_0.9.0.0_x64_${Configuration}.msix"
$pwd = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText

if (-not (Test-Path $PfxPath)) {
    Write-Error "Dev cert not found at $PfxPath. Generate one per docs/Explorer-Context-Menu.md (Local dev install section)."
    exit 1
}
if (-not (Test-Path $msix)) {
    Write-Error "Signed MSIX not found at $msix. Run 'dotnet publish src/EventLogExpert -c $Configuration /p:GenerateAppxPackageOnBuild=true /p:WindowsPackageType=MSIX' + signtool sign first."
    exit 1
}

Write-Host "[1/3] Trusting dev cert in LocalMachine\Root..."
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $pwd, "Exportable")
$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
$rootStore.Open("ReadWrite")
$rootStore.Add($cert)
$rootStore.Close()
Write-Host "   Trusted: $($cert.Thumbprint)"

Write-Host "[2/3] Removing previous install (if any)..."
Get-AppxPackage -Name "EventLogExpert" -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
Get-AppxPackage -Name "EventLogExpert" -AllUsers -ErrorAction SilentlyContinue | ForEach-Object { Remove-AppxPackage -AllUsers -Package $_.PackageFullName -ErrorAction SilentlyContinue }

Write-Host "[3/3] Installing signed MSIX..."
Add-AppxPackage -Path $msix -ForceApplicationShutdown
$installed = Get-AppxPackage -Name "EventLogExpert"
Write-Host "   Installed: $($installed.PackageFullName)  SignatureKind=$($installed.SignatureKind)  Status=$($installed.Status)"

Write-Host "[done] Restart Explorer to refresh the COM context-menu catalog:"
Write-Host "   Stop-Process -Name explorer -Force; Start-Process explorer"

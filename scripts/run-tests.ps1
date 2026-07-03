<#
.SYNOPSIS
    Runs tests via Docker Compose with Windows containers.

.DESCRIPTION
    Detects the current Docker daemon mode, switches to Windows containers if needed,
    runs the requested test suite(s), then restores the original daemon mode.

    Available suites are discovered dynamically from compose.yml services.
    Defaults to running all suites defined in compose.yml. Integration test projects
    that do not require a Windows container (e.g., Provider.Database) are not included
    in compose.yml and should be run directly via dotnet test.

.PARAMETER Suite
    Which test suite(s) to run. Accepts service names from compose.yml.
    Use 'all' (default) to run every service defined in compose.yml.
    Use tab-completion or pass any service name from compose.yml.

.EXAMPLE
    ./scripts/run-tests.ps1
    ./scripts/run-tests.ps1 -Suite eventing
    ./scripts/run-tests.ps1 -Suite runtime,elevationhelper
#>
[CmdletBinding()]
param(
    [string[]]$Suite = @("all")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$composeFile = Join-Path $repoRoot "compose.yml"

if (-not (Test-Path $composeFile)) {
    throw "compose.yml not found at '$composeFile'. Run this script from the repo root or scripts/ folder."
}

function Get-ComposeServices {
    $services = docker compose -f $composeFile config --services 2>$null

    if ($LASTEXITCODE -ne 0 -or -not $services) {
        throw "Failed to read services from compose.yml. Is Docker running?"
    }

    return $services | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
}

function Get-DockerOS {
    $info = docker info --format '{{.OSType}}' 2>$null

    if ($LASTEXITCODE -ne 0) {
        throw "Docker is not running. Start Docker Desktop and try again."
    }

    return $info.Trim()
}

function Switch-DockerDaemon([string]$targetOS) {
    $current = Get-DockerOS

    if ($current -eq $targetOS) { return $false }

    Write-Host "Switching Docker from '$current' to '$targetOS' containers..." -ForegroundColor Yellow

    $dockerCmd = Get-Command docker -ErrorAction SilentlyContinue

    if (-not $dockerCmd) {
        throw "docker is not on PATH. Install Docker Desktop and ensure it is available."
    }

    # Docker Desktop layout has changed across versions: newer installs put DockerCli.exe at
    # 'C:\Program Files\Docker\Docker\DockerCli.exe', older installs at '...\resources\DockerCli.exe'.
    # Try the canonical sibling-of-resources path first (newer layout), then the older
    # resources-relative path, then a recursive scan.
    $resourcesDir = Split-Path (Split-Path $dockerCmd.Source)
    $dockerCli = Join-Path (Split-Path $resourcesDir) "DockerCli.exe"

    if (-not (Test-Path $dockerCli)) {
        $dockerCli = Join-Path $resourcesDir "DockerCli.exe"
    }

    if (-not (Test-Path $dockerCli)) {
        $found = Get-ChildItem (Split-Path $resourcesDir) -Recurse -Filter "DockerCli.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $dockerCli = $found.FullName }
    }

    if (-not (Test-Path $dockerCli)) {
        throw "DockerCli.exe not found near '$resourcesDir'. Cannot switch Docker daemon mode."
    }

    & $dockerCli -SwitchDaemon 2>$null

    $retries = 0

    while ($retries -lt 30) {
        Start-Sleep -Seconds 2
        $retries++

        try {
            $now = Get-DockerOS

            if ($now -eq $targetOS) {
                Write-Host "Docker switched to '$targetOS' containers." -ForegroundColor Green
                return $true
            }
        } catch {
            # Docker not ready yet
        }
    }

    throw "Timed out waiting for Docker to switch to '$targetOS' containers."
}

$availableServices = @(Get-ComposeServices)

if ($availableServices.Count -eq 0) {
    throw "No services found in compose.yml."
}

$services = if ($Suite -contains "all") {
    $availableServices
} else {
    foreach ($s in $Suite) {
        if ($s -notin $availableServices) {
            throw "Suite '$s' not found in compose.yml. Available: $($availableServices -join ', ')"
        }
    }

    $Suite
}

Write-Host "Suites to run: $($services -join ', ')" -ForegroundColor Cyan
Write-Host "Available suites: $($availableServices -join ', ')" -ForegroundColor DarkGray

$originalOS = Get-DockerOS
$switched = Switch-DockerDaemon "windows"

try {
    $failed = $false

    foreach ($service in $services) {
        Write-Host "`n=== Running $service integration tests ===" -ForegroundColor Cyan

        docker compose -f $composeFile run --rm $service

        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAILED: $service" -ForegroundColor Red
            $failed = $true
        } else {
            Write-Host "PASSED: $service" -ForegroundColor Green
        }
    }

    if ($failed) {
        Write-Host "`nOne or more test suites failed." -ForegroundColor Red
    } else {
        Write-Host "`nAll test suites passed." -ForegroundColor Green
    }
} finally {
    if ($switched) {
        Write-Host "`nRestoring Docker to '$originalOS' containers..." -ForegroundColor Yellow
        Switch-DockerDaemon $originalOS | Out-Null
    }
}

if ($failed) { exit 1 }

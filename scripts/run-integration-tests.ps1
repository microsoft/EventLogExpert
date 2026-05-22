<#
.SYNOPSIS
    Runs integration tests inside Windows containers via Docker Compose.

.DESCRIPTION
    Detects the current Docker daemon mode, switches to Windows containers if needed,
    runs the requested test suite(s), then restores the original daemon mode.

.PARAMETER Suite
    Which test suite(s) to run. Default: all.
    Valid values: all, eventing, runtime, eventdbtool

.EXAMPLE
    ./tests/run-integration-tests.ps1
    ./tests/run-integration-tests.ps1 -Suite eventing
    ./tests/run-integration-tests.ps1 -Suite runtime,eventdbtool
#>
[CmdletBinding()]
param(
    [ValidateSet("all", "eventing", "runtime", "eventdbtool")]
    [string[]]$Suite = @("all")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

    & "C:\Program Files\Docker\Docker\DockerCli.exe" -SwitchDaemon 2>$null

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

$originalOS = Get-DockerOS
$switched = Switch-DockerDaemon "windows"

try {
    $services = if ($Suite -contains "all") {
        @("eventing", "runtime", "eventdbtool")
    } else {
        $Suite
    }

    $failed = $false

    foreach ($service in $services) {
        Write-Host "`n=== Running $service integration tests ===" -ForegroundColor Cyan

        docker compose run --rm $service

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

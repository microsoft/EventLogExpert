$ErrorActionPreference = 'Stop'

$svc = Get-Service eventlog -ErrorAction Stop
if ($svc.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
    Start-Service eventlog
    try {
        $svc.WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds(30))
    } catch [System.ServiceProcess.TimeoutException] {
        Write-Error -ErrorAction Continue "eventlog service failed to reach Running within 30s (status: $((Get-Service eventlog).Status))"
        exit 1
    }
}
Write-Host "eventlog service: $((Get-Service eventlog).Status)"

dotnet test @args -c Release `
    --results-directory C:\src\tests\Integration\results `
    -p:UseArtifactsOutput=true `
    -p:ArtifactsPath=C:\build\artifacts `
    -p:RunSettingsFilePath=
exit $LASTEXITCODE

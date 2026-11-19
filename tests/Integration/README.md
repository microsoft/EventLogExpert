# Integration tests

## Default behavior — integration tests are excluded by the solution runsettings filter

The solution-level `EventLogExpert.runsettings` (applied via `Directory.Build.props`) excludes
tests whose `FullyQualifiedName` contains `.IntegrationTests.`. This means:

- **VS Test Explorer "Run All Tests"** skips integration tests entirely — they do not appear as
  failed or passed; they are filtered out before discovery.
- **`dotnet test` without overrides** also excludes them.

To actually run integration tests, you must override the runsettings filter (the script and
Docker Compose commands below do this automatically).

Each integration test project also has an assembly-level `ContainerRequiredFixture` that
throws `InvalidOperationException` when the `EVENTLOG_CONTAINER` environment variable is
missing. This is the **second gate** — even if the runsettings filter is bypassed, the fixture
ensures tests fail loudly rather than silently polluting your local Application event log.

A small number of tests make an **irreversible machine-wide mutation**. The current example is the
channel-enable round-trip in `Writers/ChannelConfigWriterIntegrationTests`, which enables a real
disabled analytic/debug channel (enabling such a channel can clear its records). These require a
**third gate**: the `EVENTLOG_ALLOW_CHANNEL_MUTATION` environment variable. `compose.yml` and the
CI workflow set it automatically because those environments are ephemeral. Setting
`EVENTLOG_CONTAINER=1` alone (including via the host opt-in below) deliberately does **not** run
these tests, so a developer host is never mutated.

### To run integration tests

Use the provided script, which handles Docker daemon mode switching automatically.
The script runs all services defined in `compose.yml` (eventing, runtime, elevationhelper):

```powershell
# Run all container-gated suites
./scripts/run-tests.ps1

# Run a specific suite
./scripts/run-tests.ps1 -Suite eventing
./scripts/run-tests.ps1 -Suite runtime,elevationhelper
```

Integration test projects that do not require a Windows container (e.g., Provider.Database)
are not in `compose.yml` and should be run directly:

```powershell
$env:EVENTLOG_CONTAINER = "1"
dotnet test tests/Integration/EventLogExpert.Provider.Database.IntegrationTests/ -p:RunSettingsFilePath=""
```

The script detects the current Docker daemon mode, switches to Windows containers
if needed, runs the tests via `docker compose`, then restores the original mode.

Or run Docker Compose directly:

```powershell
docker compose run --rm eventing
docker compose run --rm runtime
docker compose run --rm elevationhelper
```

Or opt in on host explicitly (accepts event log pollution):

```powershell
$env:EVENTLOG_CONTAINER = "1"
dotnet test tests/Integration/EventLogExpert.Eventing.IntegrationTests/ -p:RunSettingsFilePath=""
```

The `-p:RunSettingsFilePath=""` override is required because `Directory.Build.props` applies
a runsettings filter that excludes integration tests by default.

CI workflows set `EVENTLOG_CONTAINER=1` and run the full suite on ephemeral runners.

## Why this directory has a `compose.yml` at the repo root

`EventLogWatcherTests` in `EventLogExpert.Eventing.IntegrationTests` writes 33
test events to the local `Application` event log. Running those tests directly
on a developer's host floods the host's real Application log with test noise.

The repo-root `compose.yml` runs each integration test project inside a Windows
container so all of those writes land in the container's own Application log,
which is destroyed when the container exits. Your host log stays clean.

## Prerequisites

- An x64 Windows 11 Pro / Enterprise / Education host, version 24H2 or later, for
  the Server 2025 base image. See Microsoft's [Windows container host compatibility
  guidance](https://learn.microsoft.com/en-us/virtualization/windowscontainers/deploy-containers/version-compatibility).
  Windows Home cannot run these containers because Hyper-V isolation is unavailable
  on the Home SKU. The image is `windows/amd64`; this configuration does not support
  Windows ARM64 hosts.
- Hyper-V and Virtual Machine Platform Windows features enabled.
- Docker Desktop for Windows, switched to **Windows containers** mode (right-click
  the Docker tray icon → *Switch to Windows containers...*).
- The first image pull requires several GB of download and additional disk space
  for unpacked layers and build outputs. Subsequent runs reuse cached layers.

Docker removes the per-test admin elevation. It does **not** remove first-time
machine provisioning — enabling Hyper-V / Virtual Machine Platform still needs
admin once.

## Quick start

Run each integration test project **serially**:

```powershell
docker compose run --rm eventing
docker compose run --rm runtime
docker compose run --rm elevationhelper
```

Do **not** use `docker compose up`. The three services share the `build-artifacts`
named volume, and MSBuild does not guarantee safe concurrent writes against the
same project artifacts. The README's invocation pattern is the only supported one.

### First run is slow

First `docker compose run --rm <service>` pulls the SDK base image, populates the
`nuget-packages` volume with restored packages, and populates `build-artifacts`
with a cold build. Subsequent runs reuse both caches.

## Output

Test results land at `tests/Integration/results/*.trx` on the host (one per
service). This directory is gitignored.

Build artifacts (obj/bin) live in the `build-artifacts` named volume, NOT on the
host. Docker uses the `UseArtifactsOutput=true` layout (`artifacts/{bin,obj,...}/<project>/...`)
to keep per-project outputs separate. Host builds (`dotnet test` directly) use the
classic `bin/obj/` layout — the two cache layouts do **not** share, which is
intentional.

## Interactive shell (for debugging a single test)

```powershell
docker compose run --rm --entrypoint powershell.exe eventing -NoLogo
```

Always pass at least one trailing argument (`-NoLogo` works). If you omit all
trailing args, Compose re-appends the service's `command:` to powershell.exe and
powershell tries to execute the csproj path as a script.

Inside the shell:

```powershell
dotnet test tests/Integration/EventLogExpert.Eventing.IntegrationTests/EventLogExpert.Eventing.IntegrationTests.csproj `
    -c Release `
    --filter "FullyQualifiedName~EventLogWatcherTests.YourSpecificTest" `
    -p:UseArtifactsOutput=true `
    -p:ArtifactsPath=C:\build\artifacts
```

## Cleanup recipes

**Targeted (after a branch switch with project renames; clears orphan binaries
but keeps NuGet cache):**

```powershell
docker volume rm dockerize-integration-tests_build-artifacts
```

(Replace `dockerize-integration-tests` with your worktree folder name, which
Compose uses as the project prefix.)

**Full nuke (re-downloads NuGet on next run — multi-GB):**

```powershell
docker compose down -v
```

## Image tag drift

`mcr.microsoft.com/dotnet/sdk:11.0-windowsservercore-ltsc2025` is a floating tag
that tracks the latest 11.0 SDK. It satisfies `global.json`'s `version: "11.0.100"`
with `rollForward: "latestFeature"`.

If you hit "A compatible .NET SDK was not found", `docker pull
mcr.microsoft.com/dotnet/sdk:11.0-windowsservercore-ltsc2025` to refresh, or pin
to a specific SDK tag (e.g. `11.0.100-windowsservercore-ltsc2025`) in
`compose.yml`.

## Host fallback (no Docker, last resort)

If you cannot use Docker (Windows Home, no Hyper-V, can't enable Windows features,
etc.), you can run the integration tests on host directly:

```powershell
dotnet test tests/Integration/EventLogExpert.Eventing.IntegrationTests/
```

**This will pollute your local Application event log with ~33 entries** from
watcher-test writes. Clean up after with:

```powershell
wevtutil cl Application    # requires elevation
```

> ⚠️ **`wevtutil cl Application` clears your ENTIRE Application log, not just
> test entries.** It may also be blocked by group policy on managed devices.
> This cleanup is intended for **disposable / dev VMs only — do not run it on
> a primary work machine** where the Application log contains diagnostic data
> you care about.

If you just cleared your Application log and re-run tests, you may hit
`SmallEvtxFixture` "log may be empty" failures (the fixture expects ≥2 records
to exist). Generate any Application event first (log off/on, restart any
service, open an app that logs to Application) and retry.

## CI is authoritative

CI on `windows-2022` runners (GitHub PR workflow + ADO OneBranch release
pipeline) is the authoritative environment regardless of which path you use
locally. Both CI environments are ephemeral, so they don't have the pollution
problem either.

Server Core inside the Docker container differs from Windows 11 in subtle ways
(reduced log/provider inventory, no UI surface). The Eventing integration tests
that touch host inventory use `SkipUnless` guards, so they skip cleanly in
Server Core rather than failing — but you'll see different skip counts vs CI.
That's expected.

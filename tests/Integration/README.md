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

### To run integration tests

Use the provided script, which handles Docker daemon mode switching automatically.
The script runs all services defined in `compose.yml` (eventing, runtime, eventdbtool):

```powershell
# Run all container-gated suites
./scripts/run-tests.ps1

# Run a specific suite
./scripts/run-tests.ps1 -Suite eventing
./scripts/run-tests.ps1 -Suite runtime,eventdbtool
```

Integration test projects that do not require a Windows container (e.g., ProviderDatabase)
are not in `compose.yml` and should be run directly:

```powershell
$env:EVENTLOG_CONTAINER = "1"
dotnet test tests/Integration/EventLogExpert.ProviderDatabase.IntegrationTests/ -p:RunSettingsFilePath=""
```

The script detects the current Docker daemon mode, switches to Windows containers
if needed, runs the tests via `docker compose`, then restores the original mode.

Or run Docker Compose directly:

```powershell
docker compose run --rm eventing
docker compose run --rm runtime
docker compose run --rm eventdbtool
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

- Windows 11 Pro / Enterprise / Education (Windows Home cannot run Windows
  containers because Hyper-V isolation is unavailable on the Home SKU).
- Hyper-V and Virtual Machine Platform Windows features enabled.
- Docker Desktop for Windows, switched to **Windows containers** mode (right-click
  the Docker tray icon → *Switch to Windows containers...*).
- First image pull is ~5 GB compressed (~12 GB on disk). Subsequent runs reuse
  cached layers.

Docker removes the per-test admin elevation. It does **not** remove first-time
machine provisioning — enabling Hyper-V / Virtual Machine Platform still needs
admin once.

## Quick start

Run each integration test project **serially**:

```powershell
docker compose run --rm eventing
docker compose run --rm runtime
docker compose run --rm eventdbtool
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

`mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2022` is a floating tag
that tracks the latest 10.x SDK. It satisfies `global.json`'s `version: "10.0.204"
+ rollForward: "latestFeature"` for any patch ≥ 10.0.204.

If you hit "A compatible .NET SDK was not found", `docker pull
mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2022` to refresh, or pin
to a specific patch tag (e.g. `10.0.204-windowsservercore-ltsc2022`) in
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

# Architecture

## Runtime / UI Split

The `EventLogExpert.Runtime` and `EventLogExpert.UI` projects intentionally mirror each
other's folder structure where a Runtime feature slice has a rendered surface:

- **Runtime** (`Microsoft.NET.Sdk`) — Fluxor state, effects, reducers, services, and commands.
  Pure C# with no Razor dependency.
- **UI** (`Microsoft.NET.Sdk.Razor`) — Razor components that render the state managed by
  Runtime.

The SDK difference physically enforces the split. Most Runtime feature slices have a
matching UI folder that renders their state — examples: `Alerts/`, `Announcement/`,
`Banner/`, `Database/`, `DatabaseTools/`, `DebugLog/`, `DetailsPane/`, `FilterLibrary/`,
`FilterPane/`, `LogTable/`, `Menu/`, `Modal/`, `Settings/`, `StatusBar/`, `Update/`. The
mirroring is one-way pragmatic, not symmetric:

- A few Runtime slices have no UI counterpart — `EventLog/` (the log-loading pipeline
  used by every render path) and `FilterProgress/` (cross-cutting progress state).
- UI has presentation-only folders that don't correspond to a Runtime feature slice —
  `ErrorHandling/`, `FilterEditor/`, `Focus/`, `Inputs/`, `Keyboard/`, `Layout/`,
  `wwwroot/`.

## Library Dependency Graph

```
EventLogExpert.Logging               (leaf — zero project refs)
EventLogExpert.Provider              (leaf — zero project refs)

EventLogExpert.Provider.Database     → Provider + Logging
EventLogExpert.Eventing              → Provider + Logging
EventLogExpert.Filtering             → Eventing
EventLogExpert.DatabaseTools         → Eventing + Provider + Provider.Database + Logging
EventLogExpert.Runtime               → Eventing + Filtering + DatabaseTools
EventLogExpert.UI                    → Runtime
EventLogExpert.WindowsPlatform       → Eventing + Logging + Runtime
EventLogExpert.EventDbTool           → DatabaseTools + Logging
EventLogExpert.ElevationHelper       → DatabaseTools + Logging + Runtime
EventLogExpert (MAUI head)           → Provider.Database + Runtime + UI + WindowsPlatform
```

Dependencies flow downward only. `Logging` and `Provider` are true leaf libraries with zero
project references.

## Provider Library Boundaries

- **Provider** — Domain contracts: models (`ProviderDetails`, `EventModel`, `MessageModel`),
  lookup interfaces, maintenance interfaces, schema versioning, and catalog path resolution.
- **Provider.Database** — EF Core persistence: `ProviderDbContext`, migrations, serialization,
  and the concrete `ProviderDatabaseMaintenance` implementation.
- **Eventing/PublisherMetadata/** — Win32 interop for reading provider metadata from the
  registry and message DLLs. Stays in Eventing because it depends on the Interop layer.

## Platform Boundaries

- **MAUI head** (`EventLogExpert`) — the application shell. Owns the Windows
  `Package.appxmanifest`, the MAUI host bootstrap (`MauiProgram.cs`), the page-level entry
  points, and platform-specific adapters that implement Runtime-defined interfaces.
- **WindowsPlatform** — Windows-only adapters extracted from the MAUI head so unit tests
  can reference them without pulling MAUI. Includes activation handling
  (file/folder/protocol/launch), the elevated-helper host, and other Windows-only
  service implementations.
- **ElevationHelper** — Standalone executable launched out-of-process when the application
  needs to perform a database operation that requires Administrator rights. Communicates
  with the host over a named pipe. Depends on `Runtime` so it can reuse the shared
  Fluxor-free service surface for filter / database helpers.

## Event Log Performance

The log-loading pipeline (`Runtime/EventLog/` plus the `Eventing` reader and `Runtime/LogTable/`
store) is tuned to open very large logs with a fast first paint, bounded memory, and smooth
scrolling. The design intent behind each mechanism - eager first paint, reverse-read batching,
non-boxing property marshalling, bounded-parallel resolution, the segmented sorted store and
combined merge view, viewport virtualization, render-buffer reuse, and the retained
structured-field filtering model - is documented, with its owning code and its guarding tests,
in [Performance](Performance.md).

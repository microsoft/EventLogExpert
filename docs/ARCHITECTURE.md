# Architecture

## Runtime / UI Split

The `EventLogExpert.Runtime` and `EventLogExpert.UI` projects intentionally mirror each
other's folder structure. This is by design:

- **Runtime** (`Microsoft.NET.Sdk`) — Fluxor state, effects, reducers, services, and commands.
  Pure C# with no Razor dependency.
- **UI** (`Microsoft.NET.Sdk.Razor`) — Razor components that render state managed by Runtime.

The SDK difference physically enforces the split. Matching folder names (e.g., `Database/`,
`Settings/`, `FilterPane/`) indicate that a UI component renders the state managed by the
corresponding Runtime feature slice.

## Library Dependency Graph

```
EventLogExpert.Logging           (leaf — zero project refs)
EventLogExpert.Provider          (leaf — zero project refs)

EventLogExpert.Provider.Database → Provider + Logging
EventLogExpert.Eventing          → Provider + Logging
EventLogExpert.Filtering         → Eventing
EventLogExpert.Runtime           → Eventing + Filtering
EventLogExpert.UI                → Runtime
EventLogExpert.EventDbTool       → Eventing + Provider.Database
EventLogExpert (MAUI head)       → Runtime + UI + Provider.Database
```

Dependencies flow downward only. `Logging` and `Provider` are true leaf libraries
with zero project references.

## Provider Library Boundaries

- **Provider** — Domain contracts: models (`ProviderDetails`, `EventModel`, `MessageModel`),
  lookup interfaces, maintenance interfaces, schema versioning, and catalog path resolution.
- **Provider.Database** — EF Core persistence: `ProviderDbContext`, migrations, serialization,
  and the concrete `ProviderDatabaseMaintenance` implementation.
- **Eventing/PublisherMetadata/** — Win32 interop for reading provider metadata from the
  registry and message DLLs. Stays in Eventing because it depends on the Interop layer.

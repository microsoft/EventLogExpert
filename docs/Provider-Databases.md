# [EventLogExpert](Home.md)

## Provider Databases

Windows event descriptions and task categories aren't stored inside an `.evtx` file. They live in resource DLLs registered against the provider name on whichever machine produced the events. Open an `.evtx` from another machine on yours, and Windows can usually only render a placeholder like `The description for Event ID … cannot be found`.

Provider databases solve that. A `.db` file contains the resolved provider metadata (descriptions, task categories, level / keyword names, parameter strings) for a set of providers, snapshotted from a machine that does have the providers installed. Load that database into EventLogExpert and `.evtx` files captured on the source machine resolve as if you were running on the source machine.

When at least one database is enabled in `Tools` → `Settings`, providers are resolved in this order:

1. For an `.evtx` opened from a folder that also contains its sibling `LocaleMetaData/*.MTA` files, those files are consulted first (this is the primary path for self-contained exports).
2. Then the enabled databases, in load order — the first database that knows the provider wins.
3. Then the local machine's installed providers as a fallback.

Disabling a database skips it from step 2 only. Exported logs that ship with sibling `LocaleMetaData/*.MTA` files still resolve via those (step 1), and any provider not resolved by databases still falls through to local providers (step 3).

### Lifecycle (Settings → Databases)

`Tools` → `Settings` → `Databases` lists every database the app knows about. The full lifecycle is covered in [Settings](Settings.md). The states a row can be in:

| Status | Behavior |
| --- | --- |
| `Ready` | Loaded and used to resolve events. |
| `Classifying…` | Initial classification pass hasn't reached this row yet. The enable / disable toggle is suppressed until classification completes. The same state is reflected by the top-of-list `Classifying databases…` banner. |
| `Upgrade required` | Schema is older than the running build but in-place upgrade is supported. The row's `Upgrade` button performs the migration in place; a backup of the original is kept on disk. |
| `Upgrade failed` | Most recent upgrade attempt failed. Use `Retry Upgrade`, re-import, or remove. |
| `Recovery required` | A previous upgrade left a backup; the original is still safe but the row needs reconciliation before going back to `Ready`. |
| `Unrecognized` | Not a database produced by this tool. |
| `Obsolete` | A v1 / v2 schema database from a long-superseded build. Not upgradable in place; recreate with `eventdbtool create`. |
| `Classification failed` | Initial classification threw. Details in `View Logs` (see [Updates and Diagnostics](Updates-And-Diagnostics.md)). |

A `Classifying databases…` banner appears during the initial pass on app startup.

## eventdbtool

Provider databases are produced and maintained by `eventdbtool`, a CLI shipped alongside EventLogExpert in the same release as `eventdbtool.zip`. The tool runs on a Windows machine that has the providers you want captured (an Exchange Server, a SQL Server, a fresh OS install — whatever produces the events you need to read elsewhere).

Root description (verbatim from `--help`):

> Tool used to create and modify databases for use with EventLogExpert

Five subcommands.

### `eventdbtool show`

```
eventdbtool show [<source>] [--filter <regex>] [--verbose]
```

> List event providers. When no source is supplied, lists providers on the local machine. When a source is supplied, it may be a .db file created with this tool, an exported .evtx file (resolved via its sibling LocaleMetaData/*.MTA files), or a folder containing either.

| Argument / option | Description |
| --- | --- |
| `source` (optional) | A `.db` file, an exported `.evtx` file, or a folder containing `.db` and/or `.evtx` files (top-level only). |
| `--filter` | `Filter for provider names matching the specified regex string.` |
| `--verbose` | `Verbose logging. May be useful for troubleshooting.` |

### `eventdbtool create`

```
eventdbtool create <file> [<source>] [--filter <regex>] [--skip-providers-in-file <source>] [--verbose]
```

> Creates a new event database.

The `<file>` argument must end in `.db` and must not already exist.

| Argument / option | Description |
| --- | --- |
| `file` | `File to create. Must have a .db extension.` |
| `source` (optional) | `Optional provider source: a .db file, an exported .evtx file, or a folder containing .db and/or .evtx files (top-level only). When omitted, local providers on this machine are used. When supplied, ONLY the source is used (no fallback to local providers).` |
| `--filter` | `Only providers matching specified regex string will be added to the database.` |
| `--skip-providers-in-file` | `Any providers found in the specified source (a .db file, an exported .evtx file, or a folder containing them, top-level only) will not be included in the new database. For example, when creating a database of event providers for Exchange Server, it may be useful to provide a database of all providers from a fresh OS install with no other products. That way, all the OS providers are skipped, and only providers added by Exchange or other installed products would be saved in the new database.` |
| `--verbose` | `Enable verbose logging. May be useful for troubleshooting.` |

### `eventdbtool merge`

```
eventdbtool merge <source> <target db> [--overwrite] [--verbose]
```

> Copies providers from a source into a target database.

The target must already exist and be at the current schema version (run `upgrade` first if not).

| Argument / option | Description |
| --- | --- |
| `source` | `The provider source: a .db file, an exported .evtx file, or a folder containing .db and/or .evtx files (top-level only).` |
| `target db` | `The target database.` |
| `--overwrite` | `When a provider from the source already exists in the target, overwrite the target data with the source data. The default is to skip providers that already exist.` |
| `--verbose` | `Enable verbose logging. May be useful for troubleshooting.` |

### `eventdbtool diff`

```
eventdbtool diff <first source> <second source> <new db> [--verbose]
```

> Given two provider sources (each may be a .db, an exported .evtx, or a folder containing them), produces a database containing all providers from the second source which are not in the first source.

| Argument / option | Description |
| --- | --- |
| `first source` | `The first source to compare: a .db, an exported .evtx, or a folder containing .db and/or .evtx files (top-level only).` |
| `second source` | `The second source to compare: a .db, an exported .evtx, or a folder containing .db and/or .evtx files (top-level only).` |
| `new db` | `The new database containing only the providers in the second source which are not in the first source. Must have a .db extension.` |
| `--verbose` | `Verbose logging. May be useful for troubleshooting.` |

### `eventdbtool upgrade`

```
eventdbtool upgrade <file> [--verbose]
```

> Upgrades the database schema

No-op when the database is already at the current version. v1 / v2 databases are not upgradable in place — recreate them with `eventdbtool create`.

| Argument / option | Description |
| --- | --- |
| `file` | `The database file to upgrade.` |
| `--verbose` | `Verbose logging. May be useful for troubleshooting.` |

[Docs home](Home.md)

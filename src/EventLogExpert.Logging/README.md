# EventLogExpert.Logging

A small, dependency-light structured logging library. Its only package reference is
`Microsoft.Extensions.Logging.Abstractions` (for `LogLevel`), so it can be copied into another solution and
used on its own.

It provides a simple write API (`ITraceLogger`), a set of destinations (`ILogSink`), and category-aware
level routing. Application-specific concerns (where the log file lives, reading the file back, wiring to a
settings service) live in the consuming application, not here.

## Vocabulary

One consistent set of terms is used throughout. Keep to them so the pieces don't read like different logging
systems.

| Term | Type | Meaning |
| --- | --- | --- |
| **Logger** | `ITraceLogger` | The write API that application code injects and calls (`Information`, `Warning`, `Error`, ...). |
| **Sink** | `ILogSink` | A destination a record is written to (console, file, UI stream). |
| **Category** | `string` (e.g. `LogRecord.Category`) | The logical grouping of a message. Routing keys on it. |
| **ProcessOrigin** | `enum` | Which process emitted the record (`InProcess` / `ElevatedHelper`). |
| **Routing** | `LogRoutingPolicy` | Maps a category to a minimum `LogLevel`, over a live global baseline. |
| **LogRecord** | `record` | One log event: timestamp, level, message, category, process origin. |

Note: "Category" is the logical grouping. "ProcessOrigin" is the emitting process. They are different things;
do not conflate them.

## Core types

### Abstractions
- `LogRecord(DateTime TimestampUtc, LogLevel Level, string Message, string Category = "", ProcessOrigin ProcessOrigin = InProcess)`
- `ProcessOrigin { InProcess, ElevatedHelper }`
- `ITraceLogger` - the logger. Level methods take interpolated-string handlers, so a call below the logger's
  `MinimumLevel` allocates no string:
  ```csharp
  logger.Information($"Loaded {count} rows for {name}");   // formatted only if Information is enabled
  ```
- `ILogSourceFactory.ForCategory(string category)` - returns an `ITraceLogger` stamped with that category.
- `ITraceLogger.ForCategory(string category)` - derives a logger that stamps `category` on its records while
  sharing the original's sinks (a `ForContext`-style re-categorization). The default implementation returns the
  same instance, so a logger that supports categories must override it.
- `LogCategories` - where THIS solution centralizes its category-name constants (roots: `App`, `Database`,
  `DatabaseTools`, `Elevation`, `EventLog`, `Offline`, `Resolution`, each with dotted sub-categories) so multiple
  executables route and filter consistently. The shipped defaults throttle the verbose roots (`Database`,
  `DatabaseTools`, `Offline`, `Resolution`) to `Warning` in the file sink (channel-authoritative). `EventLog` is
  intentionally NOT throttled - its operational detail is Debug-level, so it follows the global level (reachable at
  Debug/Trace) while its `Warning`/`Error` still surface at the default. When reusing the library in
  another project, replace these with your own (the routing itself treats categories as opaque strings).

### Sinks (`ILogSink { void Emit(LogRecord); LogLevel MinimumLevelFor(string category); }`)
- `ConsoleSink(LogLevel minimumLevel = Information)` - colored console output.
- `FileLogSink(string path, LogRoutingPolicy routingPolicy, Func<LogRecord,string> formatter)` - a pure,
  write-only file sink. It formats each record via the injected `formatter`, appends to a shared file, and
  serializes in-process writes with a lock and cross-process writes with a named mutex derived from the
  canonical path. Reading the file back is deliberately NOT part of the sink (see "Reading logs" below).
  `EmitUnfiltered` writes regardless of the routing threshold, for fatal records that must always persist.
- `UiStreamingSink(IProgress<LogRecord> progress, LogLevel minimumLevel)` - forwards records to an
  `IProgress<LogRecord>` (e.g. a UI progress consumer).

### Routing
- `LogRoutingPolicy(LoggingOptions options, LogLevel globalBaseline)` - `FileMinimumFor(category)` resolves a
  category to a minimum level by precedence: runtime overrides, then the shipped throttle for the longest
  matching category prefix (dot-segment: `Offline` covers `Offline.Wim` but not `OfflineExtras`), then the live
  global baseline. `UpdateGlobalBaseline(level)` changes the baseline at runtime. `SetCategoryOverride(category,
  level?)` sets (or clears, with `null`) a runtime per-category override that beats the shipped throttle - e.g. a
  verbose-troubleshooting toggle raising `Resolution` (and its `Resolution.*` sub-categories) to `Trace` on
  demand. A broad runtime prefix shadows a narrower shipped override regardless of specificity (first matching
  tier wins), so use it for opt-in diagnostics, not as a floor. Reads are lock-free; writes are serialized.
- `LogSourceFactory(IEnumerable<ILogSink> sinks, ProcessOrigin processOrigin = InProcess)` - the composition
  root's fan-out. `ForCategory(category)` produces a `DispatchingTraceLogger` that emits to every sink.
  `DefaultCategory` is `"App"`.
- `DispatchingTraceLogger` - an `ITraceLogger` that dispatches each call to all sinks; its `MinimumLevel` is
  the lowest across the sinks for the category.
- `StreamingTraceLogger(IProgress<LogRecord> progress, LogLevel minimumLevel = Information, string category = "")` -
  a lightweight `ITraceLogger` that reports each record to an `IProgress<LogRecord>` (used for per-operation
  progress). An empty `category` lets a downstream `CompositeLogSink` stamp the operation category.
- `CompositeLogSink(IReadOnlyList<ILogSink> sinks, string category)` - an `IProgress<LogRecord>` that stamps a
  category and fans out to several sinks (used to build a per-operation logger that also writes to the shared
  file sink).

### Configuration
- `LoggingOptions { Dictionary<string, LogSinkOptions> Sinks }` + `LogSinkOptions { Dictionary<string, LogLevel> Categories }` -
  per-sink, per-category level throttles. `LoggingOptions.FileSink` is the config KEY name for the file sink's
  throttle map (a data key, not a type name). `CreateShippedDefaults()` builds this solution's defaults.

## Composing a logger

Sinks are pure; all wiring lives in the composition root.

```csharp
var minimumLevel = verbose ? LogLevel.Trace : LogLevel.Information;

// Console-only (e.g. a CLI tool):
var factory = new LogSourceFactory([new ConsoleSink(minimumLevel)]);
ITraceLogger logger = factory.ForCategory(LogSourceFactory.DefaultCategory);

logger.Information($"Started");

// Obtain a differently-categorized logger from the same factory:
ITraceLogger databaseLogger = factory.ForCategory("Database");
```

For per-category routing plus a file sink:

```csharp
var routing = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), settings.LogLevel);

var sinks = new List<ILogSink> { new FileLogSink(logPath, routing, MyFormatter.Format) };
#if DEBUG
sinks.Add(new ConsoleSink());
#endif

var factory = new LogSourceFactory(sinks);
ITraceLogger logger = factory.ForCategory(LogCategories.DatabaseTools);
```

The `formatter` (`Func<LogRecord,string>`) is supplied by the application so the library stays format-agnostic;
the application also owns the matching parser if it reads the file back.

## Reading logs

By design this library does not read the log file back - that is an application concern (matching Serilog /
NLog / Microsoft.Extensions.Logging, none of which provide a read/tail API). In EventLogExpert the read/clear
side is `EventLogExpert.Runtime.DebugLog.IDebugLogReader` (`LoadAsync` streams the file; `ClearAsync` delegates
to the file sink so truncation coordinates with the sink's writer and mutex).

## Multi-process file logging

`FileLogSink` derives a named `Mutex` from the canonical file path (SHA256 of the upper-invariant full path),
so a main process and a helper process independently derive the same mutex and serialize their writes to one
shared file. This mirrors Serilog's shared-file sink.

## How EventLogExpert composes it

The MAUI head registers one shared `FileLogSink` singleton, exposes the read side as `IDebugLogReader`, and
force-resolves a `DebugLogHost` at startup that bridges the user's log-level setting to
`LogRoutingPolicy.UpdateGlobalBaseline` and the verbose-resolution setting to
`LogRoutingPolicy.SetCategoryOverride`, and persists unhandled exceptions via `FileLogSink.EmitUnfiltered`. The
eventdbtool CLI composes a console-only `LogSourceFactory`. Both share this one library.

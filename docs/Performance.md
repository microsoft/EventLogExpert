# Event Log Performance

EventLogExpert is built to open very large logs (hundreds of thousands to millions of
events), paint the newest rows almost immediately, and stay responsive while scrolling,
sorting, grouping, and filtering - without holding the whole rendered log in memory twice.
This page maps each performance goal to the concrete mechanism that delivers it and to the
code that owns it, so the design intent survives future refactors.

## The load pipeline (P1-P6)

Loading a log is a streaming pipeline: read raw records from `wevtapi.dll`, resolve each into
a display-ready `ResolvedEvent`, and merge the results into a sorted, virtualized table. Six
mechanisms keep it fast.

### P1 - Eager first paint

**Problem:** waiting for a whole log to load before showing anything feels broken on a large
log. **Mechanism:** the newest screenful is dispatched as soon as it is resolved. Reading is
newest-first, and once `OpenLogEffects.EagerFirstPaintThreshold` (200) events are resolved the
first paint fires, so the newest rows appear in about a second instead of waiting for the
partial-load timer. The remaining events continue streaming in behind that first paint.

### P2 - Reverse read and the EvtNext batch gate

**Problem:** the interesting events are the newest, but the Windows API enumerates oldest-first,
and the batch size that maximizes `EvtNext` throughput is version-dependent. **Mechanism:**
`EventLogReader` reads in reverse (newest-first) and captures the newest event's bookmark once,
which is also the correct resume point for a live-tail watcher. The `EvtNext` batch size
(`OpenLogEffects.ReadBatchSize`, 256) is the benchmarked Windows 11 sweet spot; larger sizes
regress. Pre-Windows 11 a batch was capped at the smaller of the requested count and a 2 MB
buffer (about 30 max-size events), so the reader requests a larger batch on Windows 11+, where
`EvtNext` simply returns fewer when the 2 MB buffer fills first.

### P3 - Zero-allocation property marshalling

**Problem:** every event carries several rendered `EventData` values; boxing each one on the
hot read path produces gigabytes of garbage over a large log. **Mechanism:** `EventProperty`
stores a rendered property without boxing - numeric, bool, and `DateTime` kinds pack into a
64-bit field tagged by a shared per-kind sentinel, while reference shapes (string, byte[], Guid,
SID, arrays, handle) live in the object slot. The marshalling path in `NativeMethods.Evt`
converts the common scalar variants directly into that packed form and only falls back to the
boxing converter for genuine reference shapes.

### P4 - Bounded parallel resolution

**Problem:** resolving descriptions and metadata is CPU-bound and embarrassingly parallel, but
unbounded parallelism starves the UI thread and thrashes the provider caches. **Mechanism:**
resolution runs under a global gate (`OpenLogEffects` uses a `PrioritySemaphore` sized to
`ProcessorCount - 1`), so multiple readers resolve in parallel while leaving a core for the UI,
and foreground work can jump the queue ahead of background prefetch.

### P5 - Segmented sorted store and the combined merge view

**Problem:** a sorted, growing list that is re-sorted or copied on every append is O(n^2) over a
streaming load; a combined (multi-log) view must stay globally sorted without materializing a
single giant array. **Mechanism:** `SegmentedSortedList` keeps immutable, globally
non-interleaving sorted segments (segment *i* entirely `<=` segment *i+1*), so the logical
sequence is their concatenation and indexing is a prefix-sum binary search; an append that sits
before or after the existing data adds a segment with no copy, and only a genuinely interleaving
append falls back to a merge. `CombinedEventView` merges several such lists with a K-way cursor
walk plus a periodic checkpoint index (stride 64) so a positional read seeks in log(segments)
rather than walking from the start, with per-read offset scratch stack-allocated up to a capped
K.

### P6 - Viewport virtualization and render-buffer reuse

**Problem:** rendering every row of a million-event table, or re-seeking the K-way merge once per
visible row, defeats the streaming store. **Mechanism:** the ungrouped table uses a Blazor
`Virtualize` component whose `ItemsProvider` (`LogTablePane.ComputeEventViewport`) fetches one
`Slice` per viewport window - a single cursor walk - instead of an `Items=` binding that re-seeks
per row. Underneath, `NativeMethods.Evt` renders each event into a per-thread, grow-only scratch
buffer: the size-probe pass is skipped (on `ERROR_INSUFFICIENT_BUFFER` the API reports the
required size, so the buffer grows once and retries), the buffer is reused across events, and a
rare huge render uses a transient array that is not stored back, bounding steady-state retention
to about 64 KB per thread. The buffers are `[ThreadStatic]` because the live-tail watcher renders
on overlapping thread-pool callbacks.

## Structured-field filtering memory model

Filtering on `EventData` and `UserData` fields (the `EventData["Field"]` / `UserData["path"]`
grammar, the built-in scenarios, and the `*` field-name globs) is served from values retained on
each `ResolvedEvent`, not by re-rendering XML per event at filter time. The retained shape is
deliberately compact:

- **EventData** keeps one `EventProperty` per field (16 bytes, packed, no boxing). Measured
  retention is roughly 2-3x smaller than retaining the rendered XML that field-filtering
  replaces.
- **UserData** (nested payloads such as CAPI2 certificate chains) is extracted once at resolve
  time into deduped `UserDataField` structs: repeats of a path collapse into a single multi-value
  field, so retention scales with distinct paths, not raw leaf count. Distinct values are interned
  and bounded.
- **Caps** bound a pathological event: a per-event distinct-path cap, a per-field value cap, and a
  per-value character cap. Hitting a cap flags the event so a filter on a dropped path stays
  visible (a keep-visible "unknown") rather than silently deciding no-match.

Rendered XML is still available on demand for the details pane, but it is never retained for
filtering.

## Mechanism status

| Area | Mechanism | Owner |
| ---- | --------- | ----- |
| First paint | Eager newest-screenful dispatch | `OpenLogEffects` |
| Read | Reverse read, newest bookmark, tuned `EvtNext` batch | `EventLogReader` |
| Marshalling | Non-boxing packed `EventProperty` | `EventProperty`, `NativeMethods.Evt` |
| Resolve | `ProcessorCount - 1` priority-gated parallelism | `OpenLogEffects` |
| Store | Segmented sorted list + K-way combined view | `SegmentedSortedList`, `CombinedEventView` |
| Viewport | `Virtualize` + one-slice-per-window provider | `LogTablePane` |
| Render | Per-thread grow-only render buffer, skip-probe | `NativeMethods.Evt` |
| Filter memory | Retained structured `EventData` / `UserData` fields | `ResolvedEvent`, `UserDataValueExtractor` |

## How performance is guarded

There is no separate benchmark harness in the repository; the performance-critical shapes are
guarded by deterministic tests that fail if a regression bloats them:

- **Retained-bytes budgets** lock the per-field retained struct sizes (`EventProperty` is 16
  bytes, `UserDataField` is within its budget) and the caps that bound a pathological event
  (`RetainedBytesBudgetTests`, `EventFieldValueTests`).
- **Allocation budgets** assert that hot predicates allocate nothing per evaluation
  (`AllocationUtils` + the filter compilation tests).
- **Slice and viewport** correctness is pinned by the segmented-store, combined-view, and
  `ComputeEventViewport` tests, so the O(log) seek behavior cannot silently degrade to a linear
  walk.

When adding a mechanism here, add or extend the matching budget/correctness test so the intent is
enforced, not just documented.

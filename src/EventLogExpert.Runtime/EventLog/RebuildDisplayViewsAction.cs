// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.EventLog;

// Effect-only continuation dispatched after IngestRawEventsAction so the rebuild reads the post-ingest store.
// BufferEntriesToConsume is the flush's captured buffer snapshot to remove on a successful rebuild; null on the
// live-tail path (no buffer to consume).
internal sealed record RebuildDisplayViewsAction(
    IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> NewEventsByLog,
    IReadOnlyList<ResolvedEvent>? BufferEntriesToConsume);

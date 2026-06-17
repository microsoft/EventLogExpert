// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

internal sealed record IngestRawEventsAction(
    IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> EventsByLog,
    RawIngestMode Mode);

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

public sealed record AppendTableEventsBatchAction(IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> EventsByLog)
{
    internal IReadOnlyDictionary<EventLogId, int> VersionByLog { get; init; } =
        ImmutableDictionary<EventLogId, int>.Empty;
}

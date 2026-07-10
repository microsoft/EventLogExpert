// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

public sealed record AppendTableEventsBatchAction
{
    internal IReadOnlyDictionary<EventLogId, EventColumnView> ViewsByLog { get; init; } =
        ImmutableDictionary<EventLogId, EventColumnView>.Empty;

    internal IReadOnlyDictionary<EventLogId, int> VersionByLog { get; init; } =
        ImmutableDictionary<EventLogId, int>.Empty;
}

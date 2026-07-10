// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

[FeatureState]
internal sealed record RawEventStoreState
{
    internal ImmutableDictionary<EventLogId, EventColumnStore> ByLog { get; init; } =
        ImmutableDictionary<EventLogId, EventColumnStore>.Empty;
}

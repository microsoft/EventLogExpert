// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

[FeatureState]
internal sealed record RawEventStoreState
{
    internal ImmutableDictionary<EventLogId, RawEventList> ByLog { get; init; } =
        ImmutableDictionary<EventLogId, RawEventList>.Empty;
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

[FeatureState]
public sealed record RawEventCountState
{
    public ImmutableDictionary<EventLogId, ProviderResolutionCounts> ByLog { get; init; } =
        ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty;

    public int Total => ByLog.Values.Sum(counts => counts.Total);
}

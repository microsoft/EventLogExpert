// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.StatusBar;

[FeatureState(MaximumStateChangedNotificationsPerSecond = 1)]
public sealed record StatusBarState
{
    public ImmutableDictionary<StatusActivityId, (int Loaded, int Failed, long? Total)> EventsLoading { get; init; } =
        ImmutableDictionary<StatusActivityId, (int Loaded, int Failed, long? Total)>.Empty;

    public string ResolverStatus { get; init; } = string.Empty;
}

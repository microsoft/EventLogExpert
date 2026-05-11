// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.StatusBar;

[FeatureState(MaximumStateChangedNotificationsPerSecond = 1)]
public sealed record StatusBarState
{
    public ImmutableDictionary<StatusActivityId, (int, int)> EventsLoading { get; init; } = ImmutableDictionary<StatusActivityId, (int, int)>.Empty;

    public string ResolverStatus { get; init; } = string.Empty;
}

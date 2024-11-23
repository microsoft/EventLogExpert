// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.StatusBar;

[FeatureState(MaximumStateChangedNotificationsPerSecond = 1)]
public sealed record StatusBarState
{
    public ImmutableDictionary<Guid, (int, int)> EventsLoading { get; init; } = ImmutableDictionary<Guid, (int, int)>.Empty;

    public string ResolverStatus { get; init; } = string.Empty;
}

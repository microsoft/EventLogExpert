// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Store.StatusBar;

[FeatureState(MaximumStateChangedNotificationsPerSecond = 1)]
public record StatusBarState
{
    public int EventsLoaded { get; init; }

    public string ResolverStatus { get; init; } = string.Empty;
}

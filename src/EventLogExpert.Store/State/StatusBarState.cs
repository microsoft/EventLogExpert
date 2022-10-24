// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Store.State;

[FeatureState(MaximumStateChangedNotificationsPerSecond = 1)]
public class StatusBarState
{
    public StatusBarState(int eventsLoaded) => EventsLoaded = eventsLoaded;

    public StatusBarState() => EventsLoaded = 0;

    public int EventsLoaded { get; }
}

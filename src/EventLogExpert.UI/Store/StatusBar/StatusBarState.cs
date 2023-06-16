// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.Store.StatusBar;

[FeatureState(MaximumStateChangedNotificationsPerSecond = 1)]
public record StatusBarState
{
    public string ResolverStatus { get; init; } = string.Empty;
}

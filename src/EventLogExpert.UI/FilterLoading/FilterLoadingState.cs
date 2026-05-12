// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.FilterLoading;

// Standalone feature state so the spinner can toggle freely; lives outside StatusBarState
// to avoid that slice's 1Hz notification throttle and its full-record reset reducer.
[FeatureState]
public sealed record FilterLoadingState
{
    public bool IsLoading { get; init; }
}

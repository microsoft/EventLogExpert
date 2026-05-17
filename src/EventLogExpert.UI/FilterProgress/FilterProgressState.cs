// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.FilterProgress;

[FeatureState]
public sealed record FilterProgressState
{
    public bool IsLoading { get; init; }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterProgress;

[FeatureState]
public sealed record FilterProgressState
{
    public bool IsLoading { get; init; }
}

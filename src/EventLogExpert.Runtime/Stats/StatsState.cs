// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Stats;

[FeatureState]
public sealed record StatsState
{
    public bool IsVisible { get; init; }
}

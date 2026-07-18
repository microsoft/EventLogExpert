// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Histogram;

[FeatureState]
public sealed record HistogramState
{
    public bool IsVisible { get; init; }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Histogram;

[FeatureState]
public sealed record HistogramState
{
    public HistogramDimensionRequest? DimensionRequest { get; init; }

    public bool IsVisible { get; init; }

    public long NextDimensionToken { get; init; }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public readonly record struct HistogramRenderBin(
    long StartTicks,
    long EndTicks,
    int Total,
    int[] GroupCounts)
{
    public bool IsAnomaly { get; init; }
}

public sealed record HistogramRender(
    IReadOnlyList<HistogramRenderBin> Bins,
    long WindowStartTicks,
    long WindowEndTicks,
    int WindowTotal,
    int MaxBinTotal,
    int[] WindowGroupTotals);

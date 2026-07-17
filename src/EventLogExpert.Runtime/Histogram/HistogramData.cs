// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

// Bucket-major: SlotCounts[bin*SlotCount + slot]; Groups folds slots into the rendered stacks.
public sealed record HistogramData(
    int[] SlotCounts,
    int SlotCount,
    int BinCount,
    DateTime MinUtc,
    DateTime MaxUtc,
    int Total,
    long BucketSpanTicks,
    IReadOnlyList<HistogramGroup> Groups)
{
    // True when the selected group-by dimension is a named EventData field that no row in the view carries, so the pane
    // shows an explicit empty-state rather than a single, meaningless "Other" band.
    public bool GroupingFieldAbsent { get; init; }
}

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
    IReadOnlyList<HistogramGroup> Groups);

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public abstract record HistogramGroupLabel
{
    private protected HistogramGroupLabel() { }

    public sealed record CategoricalOther(HistogramDimension Dimension, int FoldedCount) : HistogramGroupLabel;

    public sealed record DataValue(string Text) : HistogramGroupLabel;

    public sealed record SeverityBucket(HistogramSeverityBucket Bucket) : HistogramGroupLabel;
}

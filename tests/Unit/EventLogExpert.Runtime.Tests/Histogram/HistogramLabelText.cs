// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

internal static class HistogramLabelText
{
    internal static void AssertCategoricalOther(
        this HistogramGroup group,
        HistogramDimension expectedDimension,
        int expectedFoldedCount)
    {
        var label = Assert.IsType<HistogramGroupLabel.CategoricalOther>(group.Label);

        Assert.Equal(expectedDimension, label.Dimension);
        Assert.Equal(expectedFoldedCount, label.FoldedCount);
    }

    internal static void AssertDataValue(this HistogramGroup group, string expectedText)
    {
        var label = Assert.IsType<HistogramGroupLabel.DataValue>(group.Label);

        Assert.Equal(expectedText, label.Text);
    }

    internal static void AssertSeverityBucket(this HistogramGroup group, HistogramSeverityBucket expectedBucket)
    {
        var label = Assert.IsType<HistogramGroupLabel.SeverityBucket>(group.Label);

        Assert.Equal(expectedBucket, label.Bucket);
    }

    internal static bool HasDataValue(this HistogramGroup group, string expectedText) =>
        group.Label is HistogramGroupLabel.DataValue dataValue &&
        string.Equals(dataValue.Text, expectedText, StringComparison.Ordinal);
}

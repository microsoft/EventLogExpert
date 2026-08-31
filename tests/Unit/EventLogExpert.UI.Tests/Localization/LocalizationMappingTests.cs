// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.Localization;

public sealed class LocalizationMappingTests
{
    private readonly MarkerLocalizer _localizer = new();

    public static TheoryData<HistogramDimension, string, string> HistogramOverflowMappings() =>
        new()
        {
            { HistogramDimension.EventId, "Histogram_Overflow_EventId_One", "Histogram_Overflow_EventId_Many" },
            { HistogramDimension.Source, "Histogram_Overflow_Source_One", "Histogram_Overflow_Source_Many" },
            { HistogramDimension.TaskCategory, "Histogram_Overflow_TaskCategory_One", "Histogram_Overflow_TaskCategory_Many" },
            { HistogramDimension.Opcode, "Histogram_Overflow_Opcode_One", "Histogram_Overflow_Opcode_Many" },
            { HistogramDimension.Log, "Histogram_Overflow_Log_One", "Histogram_Overflow_Log_Many" },
            { HistogramDimension.LogonType, "Histogram_Overflow_LogonType_One", "Histogram_Overflow_LogonType_Many" },
            { HistogramDimension.TicketEncryptionType, "Histogram_Overflow_TicketEncryptionType_One", "Histogram_Overflow_TicketEncryptionType_Many" },
            { HistogramDimension.ErrorCode, "Histogram_Overflow_ErrorCode_One", "Histogram_Overflow_ErrorCode_Many" },
            { HistogramDimension.ProcessImage, "Histogram_Overflow_ProcessImage_One", "Histogram_Overflow_ProcessImage_Many" },
            { HistogramDimension.ParentProcessImage, "Histogram_Overflow_ParentProcessImage_One", "Histogram_Overflow_ParentProcessImage_Many" }
        };

    [Fact]
    public void DimensionAndHighlightLocalizers_RouteEveryEnumMemberToLiteralKey()
    {
        foreach (StatsDimension dimension in Enum.GetValues<StatsDimension>())
        {
            Assert.Equal($"[[Stats_Dimension_{dimension}]]", StatsDimensionLocalizer.Label(_localizer, dimension));
        }

        foreach (HistogramDimension dimension in Enum.GetValues<HistogramDimension>())
        {
            Assert.Equal($"[[Histogram_Dimension_{dimension}]]", HistogramDimensionLocalizer.Label(_localizer, dimension));
        }

        foreach (HighlightColor color in Enum.GetValues<HighlightColor>())
        {
            Assert.Equal($"[[Histogram_HighlightColor_{color}]]", HighlightColorLocalizer.Label(_localizer, color));
        }
    }

    [Fact]
    public void HistogramGroupLabelFormatter_PreservesDataValuesVerbatim()
    {
        Assert.Equal("Other", HistogramGroupLabelFormatter.Format(_localizer, new HistogramGroupLabel.DataValue("Other")));
    }

    [Theory]
    [MemberData(nameof(HistogramOverflowMappings))]
    public void HistogramGroupLabelFormatter_RoutesEveryCategoricalOverflowToLiteralKeys(
        HistogramDimension dimension,
        string oneKey,
        string manyKey)
    {
        Assert.Equal(
            "[[Histogram_Overflow_Bare]]",
            HistogramGroupLabelFormatter.Format(_localizer, new HistogramGroupLabel.CategoricalOther(dimension, 0)));
        Assert.Equal(
            $"[[{oneKey}(1)]]",
            HistogramGroupLabelFormatter.Format(_localizer, new HistogramGroupLabel.CategoricalOther(dimension, 1)));
        Assert.Equal(
            $"[[{manyKey}(5)]]",
            HistogramGroupLabelFormatter.Format(_localizer, new HistogramGroupLabel.CategoricalOther(dimension, 5)));
    }

    [Theory]
    [InlineData(HistogramSeverityBucket.Errors, "Histogram_Severity_Errors")]
    [InlineData(HistogramSeverityBucket.Warnings, "Histogram_Severity_Warnings")]
    [InlineData(HistogramSeverityBucket.Other, "Histogram_Severity_Other")]
    public void HistogramGroupLabelFormatter_RoutesEverySeverityBucketToLiteralKey(
        HistogramSeverityBucket bucket,
        string expectedKey)
    {
        Assert.Equal(
            $"[[{expectedKey}]]",
            HistogramGroupLabelFormatter.Format(_localizer, new HistogramGroupLabel.SeverityBucket(bucket)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void HistogramGroupLabelFormatter_ThrowsForSeverityCategoricalOther_WhichIsAnInvalidState(int foldedCount)
    {
        // Severity groups are always SeverityBucket, never CategoricalOther, so a categorical-overflow on the Severity
        // dimension is an invalid state that must fail loud rather than render a nonsensical "Other (n severities)".
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HistogramGroupLabelFormatter.Format(_localizer, new HistogramGroupLabel.CategoricalOther(HistogramDimension.Severity, foldedCount)));
    }

    [Theory]
    [InlineData(HistogramEventNoun.Events, 1, "Histogram_EventNoun_Events_One")]
    [InlineData(HistogramEventNoun.Events, 2, "Histogram_EventNoun_Events_Many")]
    [InlineData(HistogramEventNoun.ErrorCodeEvents, 1, "Histogram_EventNoun_ErrorCodeEvents_One")]
    [InlineData(HistogramEventNoun.ErrorCodeEvents, 2, "Histogram_EventNoun_ErrorCodeEvents_Many")]
    public void HistogramTextComposer_RoutesEveryEventNounToLiteralKey(
        HistogramEventNoun eventNoun,
        int count,
        string expectedKey)
    {
        Assert.Equal($"[[{expectedKey}({count})]]", HistogramTextComposer.EventNoun(_localizer, eventNoun, count));
    }

    [Fact]
    public void SeverityLevelLocalizer_UsesNeutralSeverityKeys()
    {
        foreach (SeverityLevel level in Enum.GetValues<SeverityLevel>())
        {
            Assert.Equal($"[[Severity_Level_{level}]]", SeverityLevelLocalizer.Label(_localizer, level));
        }

        Assert.Equal("[[Severity_Unknown]]", SeverityLevelLocalizer.Label(_localizer, null));
    }
}

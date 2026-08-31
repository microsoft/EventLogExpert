// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramGroupsTests
{
    [Fact]
    public void ForCategories_CategoryKeyIsStableAcrossAReRank()
    {
        // The same logical value keeps the same key regardless of its rank position, so a hidden legend entry follows the
        // category across a live-tail re-rank instead of aliasing whatever now sits at that slot.
        var first = HistogramGroups.ForCategories(["a", "b"], ["a", "b"], otherLabel: null);
        var reranked = HistogramGroups.ForCategories(["b", "a"], ["b", "a"], otherLabel: null);

        string keyWhenRankedFirst = first.First(group => group.HasDataValue("a")).Key;
        string keyWhenRankedSecond = reranked.First(group => group.HasDataValue("a")).Key;

        Assert.Equal(keyWhenRankedFirst, keyWhenRankedSecond);
    }

    [Fact]
    public void ForCategories_SyntheticOtherKeyIsDistinctFromACategoryNamedOther()
    {
        // A dimension whose top value is literally "Other" produces two groups that display the same text: the fold
        // bucket and the real category. Their label cases and keys must differ so a legend toggle hides only one.
        var groups = HistogramGroups.ForCategories(
            ["Other", "x"],
            ["Other", "x"],
            new HistogramGroupLabel.CategoricalOther(HistogramDimension.Source, 0));

        groups[0].AssertCategoricalOther(HistogramDimension.Source, expectedFoldedCount: 0);
        groups[1].AssertDataValue("Other");
        Assert.NotEqual(groups[0].Key, groups[1].Key);
    }

    [Fact]
    public void ForCategories_WhenOtherLabelIsNull_OmitsTheOtherGroup()
    {
        var groups = HistogramGroups.ForCategories(["a", "b"], ["A", "B"], otherLabel: null);

        groups[0].AssertDataValue("A");
        groups[1].AssertDataValue("B");
        Assert.DoesNotContain(groups, group => group.Key == "cat-other");
    }

    [Fact]
    public void ForCategories_WhenOtherLabelIsSupplied_UsesItForTheOtherGroup()
    {
        var groups = HistogramGroups.ForCategories(["a"], ["A"], new HistogramGroupLabel.CategoricalOther(HistogramDimension.Source, 1));

        groups[0].AssertCategoricalOther(HistogramDimension.Source, expectedFoldedCount: 1);
        Assert.Equal("cat-other", groups[0].Key);
    }

    [Fact]
    public void Severity_GroupKeysAreDistinct()
    {
        var keys = HistogramGroups.Severity.Select(group => group.Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void Severity_GroupsUseTypedBucketsStableKeysAndFoldedSlotMappingsInDisplayOrder()
    {
        IReadOnlyList<HistogramGroup> groups = HistogramGroups.Severity;

        Assert.Equal(3, groups.Count);

        groups[0].AssertSeverityBucket(HistogramSeverityBucket.Other);
        Assert.Equal("sev-other", groups[0].Key);
        Assert.Equal([0, (int)SeverityLevel.Information, (int)SeverityLevel.Verbose], groups[0].SlotIndices);

        groups[1].AssertSeverityBucket(HistogramSeverityBucket.Warnings);
        Assert.Equal("sev-warning", groups[1].Key);
        Assert.Equal([(int)SeverityLevel.Warning], groups[1].SlotIndices);

        groups[2].AssertSeverityBucket(HistogramSeverityBucket.Errors);
        Assert.Equal("sev-error", groups[2].Key);
        Assert.Equal([(int)SeverityLevel.Critical, (int)SeverityLevel.Error], groups[2].SlotIndices);
    }
}

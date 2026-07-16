// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramGroupsTests
{
    [Fact]
    public void ForCategories_CategoryKeyIsStableAcrossAReRank()
    {
        // The same logical value keeps the same key regardless of its rank position, so a hidden legend entry follows the
        // category across a live-tail re-rank instead of aliasing whatever now sits at that slot.
        var first = HistogramGroups.ForCategories(["a", "b"]);
        var reranked = HistogramGroups.ForCategories(["b", "a"]);

        string keyWhenRankedFirst = first.First(group => group.Label == "a").Key;
        string keyWhenRankedSecond = reranked.First(group => group.Label == "a").Key;

        Assert.Equal(keyWhenRankedFirst, keyWhenRankedSecond);
    }

    [Fact]
    public void ForCategories_SyntheticOtherKeyIsDistinctFromACategoryNamedOther()
    {
        // A dimension whose top value is literally "Other" produces two groups labeled "Other": the fold bucket and the real
        // category. Their keys must differ so a legend toggle hides only one.
        var groups = HistogramGroups.ForCategories(["Other", "x"]);

        Assert.Equal("Other", groups[0].Label); // synthetic fold
        Assert.Equal("Other", groups[1].Label); // real category
        Assert.NotEqual(groups[0].Key, groups[1].Key);
    }

    [Fact]
    public void Severity_GroupKeysAreDistinct()
    {
        var keys = HistogramGroups.Severity.Select(group => group.Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }
}

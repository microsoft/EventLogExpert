// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class FilterLibraryDedupKeysTests
{
    [Fact]
    public void ForFilterSet_DifferentName_ReturnsDifferentKeys()
    {
        var a = BuildFilterSet("A", BuildSavedFilter("Level == 4"));
        var b = BuildFilterSet("B", BuildSavedFilter("Level == 4"));

        Assert.NotEqual(
            FilterLibraryDedupKeys.ForFilterSet(a),
            FilterLibraryDedupKeys.ForFilterSet(b));
    }

    [Fact]
    public void ForFilterSet_DifferentTagsSameNameSameFilters_ReturnsDifferentKeys()
    {
        var a = BuildFilterSet("Set", tags: ["exchange"], BuildSavedFilter("Level == 4"));
        var b = BuildFilterSet("Set", tags: ["hub"], BuildSavedFilter("Level == 4"));

        Assert.NotEqual(
            FilterLibraryDedupKeys.ForFilterSet(a),
            FilterLibraryDedupKeys.ForFilterSet(b));
    }

    [Fact]
    public void ForFilterSet_FilterOrder_AffectsKey()
    {
        var a = BuildFilterSet("A", BuildSavedFilter("Level == 4"), BuildSavedFilter("Level == 5"));
        var b = BuildFilterSet("A", BuildSavedFilter("Level == 5"), BuildSavedFilter("Level == 4"));

        Assert.NotEqual(
            FilterLibraryDedupKeys.ForFilterSet(a),
            FilterLibraryDedupKeys.ForFilterSet(b));
    }

    [Fact]
    public void ForFilterSet_NameCasing_IsNormalizedLower()
    {
        var a = BuildFilterSet("Exchange", BuildSavedFilter("Level == 4"));
        var b = BuildFilterSet("exchange", BuildSavedFilter("Level == 4"));

        Assert.Equal(
            FilterLibraryDedupKeys.ForFilterSet(a),
            FilterLibraryDedupKeys.ForFilterSet(b));
    }

    [Fact]
    public void ForFilterSet_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FilterLibraryDedupKeys.ForFilterSet(null!));
    }

    [Fact]
    public void ForFilterSet_SameNameSameFilters_SameOrder_ReturnsEqualKeys()
    {
        var a = BuildFilterSet("A", BuildSavedFilter("Level == 4"), BuildSavedFilter("Level == 5"));
        var b = BuildFilterSet("A", BuildSavedFilter("Level == 4"), BuildSavedFilter("Level == 5"));

        Assert.Equal(
            FilterLibraryDedupKeys.ForFilterSet(a),
            FilterLibraryDedupKeys.ForFilterSet(b));
    }

    [Fact]
    public void ForFilterSet_SameTagsDifferentOrder_ReturnsEqualKeys()
    {
        var a = BuildFilterSet("Set", tags: ["beta", "alpha"], BuildSavedFilter("Level == 4"));
        var b = BuildFilterSet("Set", tags: ["alpha", "beta"], BuildSavedFilter("Level == 4"));

        Assert.Equal(
            FilterLibraryDedupKeys.ForFilterSet(a),
            FilterLibraryDedupKeys.ForFilterSet(b));
    }

    [Fact]
    public void ForFilterSetTagRelaxed_IgnoresTagDifferences()
    {
        var a = BuildFilterSet("Set", tags: ["exchange"], BuildSavedFilter("Level == 4"));
        var b = BuildFilterSet("Set", tags: ["hub"], BuildSavedFilter("Level == 4"));

        Assert.Equal(
            FilterLibraryDedupKeys.ForFilterSetTagRelaxed(a),
            FilterLibraryDedupKeys.ForFilterSetTagRelaxed(b));
    }

    [Fact]
    public void ForFilterSetTagRelaxed_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FilterLibraryDedupKeys.ForFilterSetTagRelaxed(null!));
    }

    [Fact]
    public void ForSavedFilter_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FilterLibraryDedupKeys.ForSavedFilter(null!));
    }

    [Fact]
    public void ForSavedFilter_ReturnsTupleNormalizedLowerForComparisonText()
    {
        var entry = new LibraryEntrySavedFilter
        {
            Name = "n",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = BuildSavedFilter("Level == 4"),
        };

        var key = FilterLibraryDedupKeys.ForSavedFilter(entry);

        Assert.Equal("level == 4", key.ComparisonText);
        Assert.False(key.IsExcluded);
    }

    [Fact]
    public void ForSavedFilterTagRelaxed_DifferentNameSameFilter_ReturnsDifferentKeys()
    {
        var a = BuildSavedFilterEntry("A", tags: [], "Level == 4");
        var b = BuildSavedFilterEntry("B", tags: [], "Level == 4");

        Assert.NotEqual(
            FilterLibraryDedupKeys.ForSavedFilterTagRelaxed(a),
            FilterLibraryDedupKeys.ForSavedFilterTagRelaxed(b));
    }

    [Fact]
    public void ForSavedFilterTagRelaxed_IgnoresTagDifferences()
    {
        var a = BuildSavedFilterEntry("Filter", tags: ["exchange"], "Level == 4");
        var b = BuildSavedFilterEntry("Filter", tags: ["hub"], "Level == 4");

        Assert.Equal(
            FilterLibraryDedupKeys.ForSavedFilterTagRelaxed(a),
            FilterLibraryDedupKeys.ForSavedFilterTagRelaxed(b));
    }

    [Fact]
    public void ForSavedFilterTagRelaxed_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FilterLibraryDedupKeys.ForSavedFilterTagRelaxed(null!));
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name, params SavedFilter[] filters) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. filters],
        };

    private static LibraryEntryFilterSet BuildFilterSet(string name, IEnumerable<string> tags, params SavedFilter[] filters) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. filters],
            Tags = [.. tags],
        };

    private static SavedFilter BuildSavedFilter(string comparisonText)
    {
        var filter = SavedFilter.TryCreate(comparisonText);
        Assert.NotNull(filter);
        return filter;
    }

    private static LibraryEntrySavedFilter BuildSavedFilterEntry(string name, IEnumerable<string> tags, string comparisonText) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = BuildSavedFilter(comparisonText),
            Tags = [.. tags],
        };
}

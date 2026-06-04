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

    private static LibraryEntryFilterSet BuildFilterSet(string name, params SavedFilter[] filters) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. filters],
        };

    private static SavedFilter BuildSavedFilter(string comparisonText)
    {
        var filter = SavedFilter.TryCreate(comparisonText);
        Assert.NotNull(filter);
        return filter;
    }
}

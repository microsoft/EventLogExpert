// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests.Evaluation;

public sealed class FilterExtensionsTests
{
    [Fact]
    public void HasFilteringChangedFrom_WhenArgsSwapped_ShouldReportSameChange()
    {
        // Arrange
        var first = new Filter(null, ImmutableList.Create(CreateFilter(FilterTestConstants.FilterIdEquals100)));
        var second = new Filter(null, ImmutableList.Create(CreateFilter(FilterTestConstants.FilterIdEquals200)));

        // Act
        var forward = first.HasFilteringChangedFrom(second);
        var reverse = second.HasFilteringChangedFrom(first);

        // Assert
        Assert.True(forward);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenBothEmpty_ShouldReportNoChange()
    {
        // Arrange
        var original = new Filter(null, []);
        var updated = new Filter(null, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenBothSnapshotsAreDefault_ShouldReportNoChange()
    {
        // Arrange
        var defaultFilter = default(Filter);

        // Act
        var result = defaultFilter.HasFilteringChangedFrom(default);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenComparisonTextChanges_ShouldReportChange()
    {
        // Arrange
        var first = FilterBuilder.CreateTestFilter();
        var second = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals200);

        var original = new Filter(null, ImmutableList.Create(first));
        var updated = new Filter(null, ImmutableList.Create(second));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenDateFilterAdded_ShouldReportChange()
    {
        // Arrange
        var original = new Filter(null, []);
        var dateFilter = new DateFilter { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var updated = new Filter(dateFilter, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenDateFilterRangeChanges_ShouldReportChange()
    {
        // Arrange
        var dateFilter1 = new DateFilter { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var dateFilter2 = new DateFilter { After = DateTime.Now.AddDays(-2), Before = DateTime.Now };
        var original = new Filter(dateFilter1, []);
        var updated = new Filter(dateFilter2, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenDateFilterRemoved_ShouldReportChange()
    {
        // Arrange
        var dateFilter = new DateFilter { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var original = new Filter(dateFilter, []);
        var updated = new Filter(null, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenEquivalentFiltersFromDifferentInstances_ShouldReportNoChange()
    {
        // Arrange
        var original = new Filter(
            null,
            ImmutableList.Create(CreateFilter(FilterTestConstants.FilterIdEquals100)));

        var updated = new Filter(
            null,
            ImmutableList.Create(CreateFilter(FilterTestConstants.FilterIdEquals100)));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenFilterOrderSwaps_ShouldReportChange()
    {
        // Arrange
        var first = CreateFilter(FilterTestConstants.FilterIdEquals100);
        var second = CreateFilter(FilterTestConstants.FilterIdEquals200);

        var original = new Filter(null, ImmutableList.Create(first, second));
        var updated = new Filter(null, ImmutableList.Create(second, first));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenFiltersAdded_ShouldReportChange()
    {
        // Arrange
        var original = new Filter(null, []);
        var filter = CreateFilter(FilterTestConstants.FilterIdEquals100);
        var updated = new Filter(null, [filter]);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenFiltersRemoved_ShouldReportChange()
    {
        // Arrange
        var filter = CreateFilter(FilterTestConstants.FilterIdEquals100);
        var original = new Filter(null, [filter]);
        var updated = new Filter(null, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenIsExcludedDiffers_ShouldReportChange()
    {
        // Arrange
        var original = new Filter(
            null,
            ImmutableList.Create(CreateFilter(FilterTestConstants.FilterIdEquals100)));

        var updated = new Filter(
            null,
            ImmutableList.Create(CreateFilter(FilterTestConstants.FilterIdEquals100, true)));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenOnlyColorDiffers_ShouldReportNoChange()
    {
        // Arrange
        var redFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.Red);
        var blueFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.Blue);

        var original = new Filter(null, ImmutableList.Create(redFilter));
        var updated = new Filter(null, ImmutableList.Create(blueFilter));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenOnlyDateFilterIsEnabledToggles_ShouldReportChange()
    {
        // Arrange
        var bounds = (After: DateTime.Now.AddDays(-1), Before: DateTime.Now);
        var enabled = new DateFilter { After = bounds.After, Before = bounds.Before, IsEnabled = true };
        var disabled = new DateFilter { After = bounds.After, Before = bounds.Before, IsEnabled = false };
        var original = new Filter(enabled, []);
        var updated = new Filter(disabled, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenOnlyOneSnapshotIsDefault_ShouldReportChange()
    {
        // Arrange
        var initialized = new Filter(null, []);

        // Act
        var forward = initialized.HasFilteringChangedFrom(default);
        var reverse = default(Filter).HasFilteringChangedFrom(initialized);

        // Assert
        Assert.True(forward);
        Assert.True(reverse);
    }

    [Fact]
    public void HasFilteringChangedFrom_WhenSameFilters_ShouldReportNoChange()
    {
        // Arrange
        var filters = ImmutableList.Create(CreateFilter(FilterTestConstants.FilterIdEquals100));
        var original = new Filter(null, filters);
        var updated = new Filter(null, filters);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenBothDateFilterAndFiltersExist_ShouldBeEnabled()
    {
        // Arrange
        var dateFilter = new DateFilter { IsEnabled = true };
        var savedFilter = CreateFilter(FilterTestConstants.FilterIdEquals100);
        var filter = new Filter(dateFilter, [savedFilter]);

        // Act
        var result = filter.IsFilteringEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenDateFilterDisabledAndNoFilters_ShouldBeDisabled()
    {
        // Arrange
        var dateFilter = new DateFilter { IsEnabled = false };
        var filter = new Filter(dateFilter, []);

        // Act
        var result = filter.IsFilteringEnabled;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenDateFilterDisabledButFiltersExist_ShouldBeEnabled()
    {
        // Arrange
        var dateFilter = new DateFilter { IsEnabled = false };
        var savedFilter = CreateFilter(FilterTestConstants.FilterIdEquals100);
        var filter = new Filter(dateFilter, [savedFilter]);

        // Act
        var result = filter.IsFilteringEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenDateFilterEnabled_ShouldBeEnabled()
    {
        // Arrange
        var dateFilter = new DateFilter { IsEnabled = true };
        var filter = new Filter(dateFilter, []);

        // Act
        var result = filter.IsFilteringEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenNoDateFilterAndFiltersExist_ShouldBeEnabled()
    {
        // Arrange
        var savedFilter = CreateFilter(FilterTestConstants.FilterIdEquals100);
        var filter = new Filter(null, [savedFilter]);

        // Act
        var result = filter.IsFilteringEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenNoDateFilterAndNoFilters_ShouldBeDisabled()
    {
        // Arrange
        var filter = new Filter(null, []);

        // Act
        var result = filter.IsFilteringEnabled;

        // Assert
        Assert.False(result);
    }

    private static SavedFilter CreateFilter(string expression, bool isExcluded = false) =>
        FilterBuilder.CreateTestFilter(expression, isExcluded: isExcluded);
}

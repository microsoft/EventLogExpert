// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class EventFilterExtensionsTests
{
    [Fact]
    public void HasFilteringChanged_WhenBothEmpty_ShouldReportNoChange()
    {
        // Arrange
        var original = new EventFilter(null, []);
        var updated = new EventFilter(null, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenComparisonTextChanges_ShouldReportChange()
    {
        // SavedFilter is now immutable, so structural change is the only signal HasFilteringChanged
        // needs to detect. (Pre-immutability this test guarded against in-place Comparison mutation.)
        var first = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var second = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);

        var original = new EventFilter(null, ImmutableList.Create(first));
        var updated = new EventFilter(null, ImmutableList.Create(second));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenComparisonValueDiffers_ShouldReportChange()
    {
        // Arrange
        var original = new EventFilter(null, ImmutableList.Create(CreateFilter(Constants.FilterIdEquals100)));
        var updated = new EventFilter(null, ImmutableList.Create(CreateFilter(Constants.FilterIdEquals200)));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenDateFilterAdded_ShouldReportChange()
    {
        // Arrange
        var original = new EventFilter(null, []);
        var dateFilter = new DateFilter { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var updated = new EventFilter(dateFilter, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenDateFilterRangeChanges_ShouldReportChange()
    {
        // Arrange
        var dateFilter1 = new DateFilter { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var dateFilter2 = new DateFilter { After = DateTime.Now.AddDays(-2), Before = DateTime.Now };
        var original = new EventFilter(dateFilter1, []);
        var updated = new EventFilter(dateFilter2, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenDateFilterRemoved_ShouldReportChange()
    {
        // Arrange
        var dateFilter = new DateFilter { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var original = new EventFilter(dateFilter, []);
        var updated = new EventFilter(null, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenEquivalentFiltersFromDifferentInstances_ShouldReportNoChange()
    {
        // Arrange - separately allocated FilterModels and ImmutableLists with the same Value/IsExcluded.
        // Reproduces the bug where ImmutableList<T>.Equals (reference equality) caused this to return true.
        var original = new EventFilter(
            null,
            ImmutableList.Create(CreateFilter(Constants.FilterIdEquals100)));

        var updated = new EventFilter(
            null,
            ImmutableList.Create(CreateFilter(Constants.FilterIdEquals100)));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenFiltersAdded_ShouldReportChange()
    {
        // Arrange
        var original = new EventFilter(null, []);
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var updated = new EventFilter(null, [filter]);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenFiltersRemoved_ShouldReportChange()
    {
        // Arrange
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var original = new EventFilter(null, [filter]);
        var updated = new EventFilter(null, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenIsExcludedDiffers_ShouldReportChange()
    {
        // Arrange
        var original = new EventFilter(
            null,
            ImmutableList.Create(CreateFilter(Constants.FilterIdEquals100, false)));

        var updated = new EventFilter(
            null,
            ImmutableList.Create(CreateFilter(Constants.FilterIdEquals100, true)));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenOnlyColorDiffers_ShouldReportNoChange()
    {
        // Color affects highlighting, not the filtered event set.
        var redFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, color: HighlightColor.Red);
        var blueFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, color: HighlightColor.Blue);

        var original = new EventFilter(null, ImmutableList.Create(redFilter));
        var updated = new EventFilter(null, ImmutableList.Create(blueFilter));

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenOnlyDateFilterIsEnabledToggles_ShouldReportChange()
    {
        // Arrange - pins record value-equality on DateFilter: toggling IsEnabled with identical After/Before is a structural change because IsEnabled participates in the auto-generated record Equals.
        var bounds = (After: DateTime.Now.AddDays(-1), Before: DateTime.Now);
        var enabled = new DateFilter { After = bounds.After, Before = bounds.Before, IsEnabled = true };
        var disabled = new DateFilter { After = bounds.After, Before = bounds.Before, IsEnabled = false };
        var original = new EventFilter(enabled, []);
        var updated = new EventFilter(disabled, []);

        // Act
        var result = updated.HasFilteringChangedFrom(original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenSameFilters_ShouldReportNoChange()
    {
        // Arrange
        var filters = ImmutableList.Create(CreateFilter(Constants.FilterIdEquals100));
        var original = new EventFilter(null, filters);
        var updated = new EventFilter(null, filters);

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
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var eventFilter = new EventFilter(dateFilter, [filter]);

        // Act
        var result = eventFilter.IsFilteringEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenDateFilterDisabledAndNoFilters_ShouldBeDisabled()
    {
        // Arrange
        var dateFilter = new DateFilter { IsEnabled = false };
        var eventFilter = new EventFilter(dateFilter, []);

        // Act
        var result = eventFilter.IsFilteringEnabled;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenDateFilterDisabledButFiltersExist_ShouldBeEnabled()
    {
        // Arrange - pins the OR-semantics in IsFilteringEnabled: an `||` -> `&&` regression would silently disable filtering when the date filter is toggled off.
        var dateFilter = new DateFilter { IsEnabled = false };
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var eventFilter = new EventFilter(dateFilter, [filter]);

        // Act
        var result = eventFilter.IsFilteringEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenDateFilterEnabled_ShouldBeEnabled()
    {
        // Arrange
        var dateFilter = new DateFilter { IsEnabled = true };
        var eventFilter = new EventFilter(dateFilter, []);

        // Act
        var result = eventFilter.IsFilteringEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenNoDateFilterAndFiltersExist_ShouldBeEnabled()
    {
        // Arrange
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var eventFilter = new EventFilter(null, [filter]);

        // Act
        var result = eventFilter.IsFilteringEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenNoDateFilterAndNoFilters_ShouldBeDisabled()
    {
        // Arrange
        var eventFilter = new EventFilter(null, []);

        // Act
        var result = eventFilter.IsFilteringEnabled;

        // Assert
        Assert.False(result);
    }

    private static SavedFilter CreateFilter(string expression, bool isExcluded = false) =>
        FilterUtils.CreateTestFilter(comparisonValue: expression, isExcluded: isExcluded);
}

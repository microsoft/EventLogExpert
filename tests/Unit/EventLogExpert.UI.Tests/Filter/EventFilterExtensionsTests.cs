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
    public void HasFilteringChanged_WhenBothEmpty_ShouldReturnFalse()
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
    public void HasFilteringChanged_WhenComparisonTextChanges_ShouldReturnTrue()
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
    public void HasFilteringChanged_WhenComparisonValueDiffers_ShouldReturnTrue()
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
    public void HasFilteringChanged_WhenDateFilterAdded_ShouldReturnTrue()
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
    public void HasFilteringChanged_WhenDateFilterDifferent_ShouldReturnTrue()
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
    public void HasFilteringChanged_WhenDateFilterRemoved_ShouldReturnTrue()
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
    public void HasFilteringChanged_WhenEquivalentFiltersFromDifferentInstances_ShouldReturnFalse()
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
    public void HasFilteringChanged_WhenFiltersAdded_ShouldReturnTrue()
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
    public void HasFilteringChanged_WhenFiltersRemoved_ShouldReturnTrue()
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
    public void HasFilteringChanged_WhenIsExcludedDiffers_ShouldReturnTrue()
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
    public void HasFilteringChanged_WhenOnlyColorDiffers_ShouldReturnFalse()
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
    public void HasFilteringChanged_WhenSameFilters_ShouldReturnFalse()
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
    public void IsFilteringEnabled_WhenBothDateFilterAndFiltersExist_ShouldReturnTrue()
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
    public void IsFilteringEnabled_WhenDateFilterDisabled_ShouldReturnFalse()
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
    public void IsFilteringEnabled_WhenDateFilterEnabled_ShouldReturnTrue()
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
    public void IsFilteringEnabled_WhenFiltersExist_ShouldReturnTrue()
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
    public void IsFilteringEnabled_WhenNoDateFilterAndNoFilters_ShouldReturnFalse()
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

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class FilterServiceTests
{
    [Fact]
    public void Constructor_WhenCalled_ShouldNotThrow()
    {
        // Arrange & Act
        var exception = Record.Exception(() => CreateFilterService());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void FilterActiveLogs_WhenEmptyLogData_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var filterService = CreateFilterService();
        var eventFilter = new EventFilter(null, []);

        // Act
        var result = filterService.FilterActiveLogs([], eventFilter);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FilterActiveLogs_WhenMultipleLogs_ShouldFilterEachLog()
    {
        // Arrange
        var filterService = CreateFilterService();

        var log1Events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, level: Constants.EventLevelError),
            EventUtils.CreateTestEvent(200, level: Constants.EventLevelInformation)
        };

        var log2Events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, level: Constants.EventLevelError),
            EventUtils.CreateTestEvent(300, level: Constants.EventLevelError)
        };

        var logData = new List<EventLogData>
        {
            new("Log1", PathType.LogName, log1Events),
            new("Log2", PathType.LogName, log2Events)
        };

        var eventFilter = new EventFilter(null, []);

        // Act
        var result = filterService.FilterActiveLogs(logData, eventFilter);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterActiveLogs_WhenNoFiltersEnabled_ShouldReturnAllEvents()
    {
        // Arrange
        var filterService = CreateFilterService();

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        var logData = new List<EventLogData>
        {
            new("TestLog", PathType.LogName, events)
        };

        var eventFilter = new EventFilter(null, []);

        // Act
        var result = filterService.FilterActiveLogs(logData, eventFilter);

        // Assert
        var logId = logData[0].Id;
        Assert.Equal(2, result[logId].Count);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(9_999)]
    [InlineData(10_000)]
    [InlineData(10_001)]
    public void GetFilteredEvents_WhenFilteringEnabled_ShouldFilterCorrectlyAcrossThreshold(int eventCount)
    {
        // Arrange
        var filterService = CreateFilterService();
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Create events: half before cutoff, half after
        var events = Enumerable.Range(0, eventCount)
            .Select(i => EventUtils.CreateTestEvent(
                id: i,
                timeCreated: baseTime.AddMinutes(i),
                recordId: i))
            .ToList();

        var cutoff = baseTime.AddMinutes(eventCount / 2);
        var dateFilter = new FilterDateModel { After = cutoff, Before = baseTime.AddMinutes(eventCount), IsEnabled = true };
        var eventFilter = new EventFilter(dateFilter, []);

        // Act
        var result = filterService.GetFilteredEvents(events, eventFilter);

        // Assert — should return only events at or after the cutoff
        var expectedCount = events.Count(e => e.TimeCreated >= cutoff && e.TimeCreated <= baseTime.AddMinutes(eventCount));
        Assert.Equal(expectedCount, result.Count);
        Assert.All(result, e => Assert.True(e.TimeCreated >= cutoff));
    }

    [Fact]
    public void GetFilteredEvents_WhenFilteringSmallCollection_ShouldReturnSameResultsAsLargeCollection()
    {
        // Arrange — verify both paths produce identical results
        var filterService = CreateFilterService();
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = baseTime.AddMinutes(50);

        var dateFilter = new FilterDateModel { After = cutoff, Before = baseTime.AddMinutes(200), IsEnabled = true };
        var eventFilter = new EventFilter(dateFilter, []);

        // Small collection (sequential path)
        var smallEvents = Enumerable.Range(0, 100)
            .Select(i => EventUtils.CreateTestEvent(id: i, timeCreated: baseTime.AddMinutes(i), recordId: i))
            .ToList();

        // Large collection (PLINQ path) with the same first 100 events
        var largeEvents = Enumerable.Range(0, 15_000)
            .Select(i => EventUtils.CreateTestEvent(id: i, timeCreated: baseTime.AddMinutes(i), recordId: i))
            .ToList();

        // Act
        var smallResult = filterService.GetFilteredEvents(smallEvents, eventFilter);
        var largeResult = filterService.GetFilteredEvents(largeEvents, eventFilter);

        // Assert — small result should match the first 100 events' filtered subset
        var expectedSmallCount = smallEvents.Count(e => e.TimeCreated >= cutoff && e.TimeCreated <= baseTime.AddMinutes(200));
        Assert.Equal(expectedSmallCount, smallResult.Count);

        // Large result should include the same events as small result (plus more from the larger set)
        Assert.True(largeResult.Count > smallResult.Count);
        Assert.All(smallResult, e => Assert.Contains(e, largeResult));
    }

    [Fact]
    public void GetFilteredEvents_WhenNoFiltersEnabled_ShouldReturnAllEvents()
    {
        // Arrange
        var filterService = CreateFilterService();

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200),
            EventUtils.CreateTestEvent(300)
        };

        var eventFilter = new EventFilter(null, []);

        // Act
        var result = filterService.GetFilteredEvents(events, eventFilter);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void IsXmlEnabled_WhenStateIsFalse_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService(false);

        // Act
        var result = filterService.IsXmlEnabled;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsXmlEnabled_WhenStateIsTrue_ShouldReturnTrue()
    {
        // Arrange
        var filterService = CreateFilterService(true);

        // Act
        var result = filterService.IsXmlEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void TryParse_WhenCategoryIsXmlAndXmlDisabled_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService(false);
        var filterModel = CreateFilterModel(FilterCategory.Xml, FilterEvaluator.Contains, "test");

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.False(result);
        Assert.Empty(comparison);
    }

    [Fact]
    public void TryParse_WhenCategoryIsXmlAndXmlEnabled_ShouldReturnTrue()
    {
        // Arrange
        var filterService = CreateFilterService(true);
        var filterModel = CreateFilterModel(FilterCategory.Xml, FilterEvaluator.Contains, "test");

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.NotEmpty(comparison);
    }

    [Theory]
    [InlineData(FilterCategory.Description,
        FilterEvaluator.Contains,
        "error",
        "Description.Contains(\"error\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(FilterCategory.Source,
        FilterEvaluator.Contains,
        "Test",
        "Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    public void TryParse_WhenContainsEvaluator_ShouldGenerateCorrectComparison(
        FilterCategory category,
        FilterEvaluator evaluator,
        string value,
        string expectedComparison)
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(category, evaluator, value);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryParse_WhenEmptyValueAndNotMultiSelect_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Id, FilterEvaluator.Equals, null);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.False(result);
        Assert.Empty(comparison);
    }

    [Fact]
    public void TryParse_WhenEmptyValuesAndMultiSelect_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Id, FilterEvaluator.MultiSelect, null);
        filterModel.Data.Values.Clear();

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.False(result);
        Assert.Empty(comparison);
    }

    [Theory]
    [InlineData(FilterCategory.Id, FilterEvaluator.Equals, "100", "Id == \"100\"")]
    [InlineData(FilterCategory.Level, FilterEvaluator.Equals, "Error", "Level == \"Error\"")]
    [InlineData(FilterCategory.Source, FilterEvaluator.Equals, "TestSource", "Source == \"TestSource\"")]
    public void TryParse_WhenEqualsEvaluator_ShouldGenerateCorrectComparison(
        FilterCategory category,
        FilterEvaluator evaluator,
        string value,
        string expectedComparison)
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(category, evaluator, value);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryParse_WhenIdToStringContains_ShouldGenerateCorrectComparison()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Id, FilterEvaluator.Contains, "10");

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal("Id.ToString().Contains(\"10\", StringComparison.OrdinalIgnoreCase)", comparison);
    }

    [Fact]
    public void TryParse_WhenKeywordsEquals_ShouldGenerateAnyComparison()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Keywords, FilterEvaluator.Equals, "Audit Success");

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("Keywords.Any", comparison);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", comparison);
    }

    [Fact]
    public void TryParse_WhenMultiSelectWithValues_ShouldGenerateContainsComparison()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Level, FilterEvaluator.MultiSelect, null);
        filterModel.Data.Values.AddRange(["Error", "Warning"]);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("Error", comparison);
        Assert.Contains("Warning", comparison);
        Assert.Contains("Contains", comparison);
    }

    [Theory]
    [InlineData(FilterCategory.Description,
        FilterEvaluator.NotContains,
        "error",
        "!Description.Contains(\"error\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(FilterCategory.Source,
        FilterEvaluator.NotContains,
        "Test",
        "!Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    public void TryParse_WhenNotContainsEvaluator_ShouldGenerateCorrectComparison(
        FilterCategory category,
        FilterEvaluator evaluator,
        string value,
        string expectedComparison)
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(category, evaluator, value);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Theory]
    [InlineData(FilterCategory.Id, FilterEvaluator.NotEqual, "100", "Id != \"100\"")]
    [InlineData(FilterCategory.Level, FilterEvaluator.NotEqual, "Error", "Level != \"Error\"")]
    public void TryParse_WhenNotEqualEvaluator_ShouldGenerateCorrectComparison(
        FilterCategory category,
        FilterEvaluator evaluator,
        string value,
        string expectedComparison)
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(category, evaluator, value);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryParse_WhenQuotesInValue_ShouldEscapeQuotes()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Description, FilterEvaluator.Contains, "test\"value");

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("test'value", comparison); // Quotes should be replaced with single quotes
    }

    [Fact]
    public void TryParse_WhenSubFiltersExist_ShouldAppendSubFilterComparison()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Id, FilterEvaluator.Equals, "100");

        var subFilter = CreateFilterModel(FilterCategory.Level, FilterEvaluator.Equals, "Error");
        filterModel.SubFilters.Add(subFilter);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("Id == \"100\"", comparison);
        Assert.Contains("Level == \"Error\"", comparison);
    }

    [Fact]
    public void TryParse_WhenSubFilterWithCompareAny_ShouldUseOrOperator()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Id, FilterEvaluator.Equals, "100");

        var subFilter = CreateFilterModel(FilterCategory.Level, FilterEvaluator.Equals, "Error");
        subFilter.ShouldCompareAny = true;
        filterModel.SubFilters.Add(subFilter);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains(" || ", comparison);
    }

    [Fact]
    public void TryParse_WhenSubFilterWithoutCompareAny_ShouldUseAndOperator()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.Id, FilterEvaluator.Equals, "100");

        var subFilter = CreateFilterModel(FilterCategory.Level, FilterEvaluator.Equals, "Error");
        subFilter.ShouldCompareAny = false;
        filterModel.SubFilters.Add(subFilter);

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains(" && ", comparison);
    }

    [Fact]
    public void TryParse_WhenUserIdEquals_ShouldIncludeNullCheck()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filterModel = CreateFilterModel(FilterCategory.UserId, FilterEvaluator.Equals, "S-1-5-21");

        // Act
        var result = filterService.TryParse(filterModel, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("UserId != null", comparison);
    }

    [Theory]
    [InlineData("Id == 100")]
    [InlineData("Level == \"Error\"")]
    [InlineData("Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Id > 100 && Level == \"Error\"")]
    [InlineData("Id == 100 || Id == 200")]
    public void TryParseExpression_WhenCommonExpressions_ShouldReturnTrue(string expression)
    {
        // Arrange
        var filterService = CreateFilterService();

        // Act
        var result = filterService.TryParseExpression(expression, out var error);

        // Assert
        Assert.True(result);
        Assert.Empty(error);
    }

    [Fact]
    public void TryParseExpression_WhenEmptyExpression_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService();

        // Act
        var result = filterService.TryParseExpression(string.Empty, out var error);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryParseExpression_WhenInvalidExpression_ShouldReturnFalseWithError()
    {
        // Arrange
        var filterService = CreateFilterService();

        // Act
        var result = filterService.TryParseExpression(Constants.FilterInvalidProperty, out var error);

        // Assert
        Assert.False(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryParseExpression_WhenNullExpression_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService();

        // Act
        var result = filterService.TryParseExpression(null, out var error);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryParseExpression_WhenValidExpression_ShouldReturnTrue()
    {
        // Arrange
        var filterService = CreateFilterService();

        // Act
        var result = filterService.TryParseExpression(Constants.FilterIdEquals100, out var error);

        // Assert
        Assert.True(result);
        Assert.Empty(error);
    }

    [Fact]
    public void TryParseExpression_WhenXmlExpressionAndXmlDisabled_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService(false);

        // Act
        var result = filterService.TryParseExpression("Xml.Contains(\"test\")", out var error);

        // Assert
        Assert.False(result);
        Assert.Contains("Xml filtering is not enabled", error);
    }

    [Fact]
    public void TryParseExpression_WhenXmlExpressionAndXmlEnabled_ShouldReturnTrue()
    {
        // Arrange
        var filterService = CreateFilterService(true);

        // Act
        var result = filterService.TryParseExpression("Xml.Contains(\"test\", StringComparison.OrdinalIgnoreCase)",
            out var error);

        // Assert
        Assert.True(result);
        Assert.Empty(error);
    }

    [Fact]
    public void TryParseExpression_WhenXmlExpressionWithIgnoreXml_ShouldReturnTrue()
    {
        // Arrange
        var filterService = CreateFilterService(false);

        // Act
        var result = filterService.TryParseExpression("Xml.Contains(\"test\", StringComparison.OrdinalIgnoreCase)",
            out var error,
            true);

        // Assert
        Assert.True(result);
        Assert.Empty(error);
    }

    private static FilterModel CreateFilterModel(FilterCategory category, FilterEvaluator evaluator, string? value) =>
        new() { Data = { Category = category, Evaluator = evaluator, Value = value } };

    private static FilterService CreateFilterService(bool isXmlEnabled = false)
    {
        var mockState = Substitute.For<IState<FilterPaneState>>();
        mockState.Value.Returns(new FilterPaneState { IsXmlEnabled = isXmlEnabled });

        return new FilterService(mockState);
    }
}

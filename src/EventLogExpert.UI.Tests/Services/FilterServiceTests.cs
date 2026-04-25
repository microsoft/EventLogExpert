// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Services;

public sealed class FilterServiceTests
{
    [Fact]
    public void Constructor_WhenCalled_ShouldNotThrow()
    {
        // Arrange & Act
        var exception = Record.Exception(CreateFilterService);

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
    public void FilterActiveLogs_WhenMultipleLargeLogs_ShouldMatchSequentialResult()
    {
        // Arrange — two logs whose combined size exceeds the outer-parallel threshold (10k);
        // verifies the parallel path returns the same per-log results as the sequential path.
        var filterService = CreateFilterService();
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = baseTime.AddMinutes(3_000);

        var dateFilter = new FilterDateModel { After = cutoff, Before = baseTime.AddMinutes(20_000), IsEnabled = true };
        var eventFilter = new EventFilter(dateFilter, []);

        var log1Events = Enumerable.Range(0, 6_000)
            .Select(i => EventUtils.CreateTestEvent(id: i, timeCreated: baseTime.AddMinutes(i), recordId: i))
            .ToList();

        var log2Events = Enumerable.Range(0, 6_000)
            .Select(i => EventUtils.CreateTestEvent(id: i, timeCreated: baseTime.AddMinutes(i + 1_000), recordId: i + 6_000))
            .ToList();

        var logData = new List<EventLogData>
        {
            new("Log1", PathType.LogName, log1Events),
            new("Log2", PathType.LogName, log2Events)
        };

        var expectedLog1 = log1Events.Where(e => e.TimeCreated >= cutoff && e.TimeCreated <= baseTime.AddMinutes(20_000)).ToList();
        var expectedLog2 = log2Events.Where(e => e.TimeCreated >= cutoff && e.TimeCreated <= baseTime.AddMinutes(20_000)).ToList();

        // Act
        var result = filterService.FilterActiveLogs(logData, eventFilter);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(expectedLog1.Count, result[logData[0].Id].Count);
        Assert.Equal(expectedLog2.Count, result[logData[1].Id].Count);
        Assert.Equal(expectedLog1.Select(e => e.RecordId), result[logData[0].Id].Select(e => e.RecordId));
        Assert.Equal(expectedLog2.Select(e => e.RecordId), result[logData[1].Id].Select(e => e.RecordId));
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
    public void FilterActiveLogs_WhenMultipleLogsButFilteringDisabled_ShouldReturnAllEventsPerLog()
    {
        // Arrange — IsFilteringEnabled short-circuit must stay engaged even with many logs to
        // avoid spinning up parallel work for a no-op filter.
        var filterService = CreateFilterService();

        var log1Events = Enumerable.Range(0, 6_000).Select(i => EventUtils.CreateTestEvent(i)).ToList();
        var log2Events = Enumerable.Range(0, 6_000).Select(i => EventUtils.CreateTestEvent(i)).ToList();

        var logData = new List<EventLogData>
        {
            new("Log1", PathType.LogName, log1Events),
            new("Log2", PathType.LogName, log2Events)
        };

        var eventFilter = new EventFilter(null, []);

        // Act
        var result = filterService.FilterActiveLogs(logData, eventFilter);

        // Assert
        Assert.Equal(log1Events.Count, result[logData[0].Id].Count);
        Assert.Equal(log2Events.Count, result[logData[1].Id].Count);
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
    public void TryParse_WhenCategoryIsXml_ShouldReturnTrue()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = CreateBasicFilter(FilterCategory.Xml, FilterEvaluator.Contains, "test");

        // Act
        var result = filterService.TryParse(source, out var comparison);

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
        var source = CreateBasicFilter(category, evaluator, value);

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryParse_WhenEmptyValueAndNotMultiSelect_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = CreateBasicFilter(FilterCategory.Id, FilterEvaluator.Equals, null);

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.False(result);
        Assert.Empty(comparison);
    }

    [Fact]
    public void TryParse_WhenEmptyValuesAndMultiSelect_ShouldReturnFalse()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = CreateBasicFilter(FilterCategory.Id, FilterEvaluator.MultiSelect, null);

        // Act
        var result = filterService.TryParse(source, out var comparison);

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
        var source = CreateBasicFilter(category, evaluator, value);

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryParse_WhenIdToStringContains_ShouldGenerateCorrectComparison()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = CreateBasicFilter(FilterCategory.Id, FilterEvaluator.Contains, "10");

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal("Id.ToString().Contains(\"10\", StringComparison.OrdinalIgnoreCase)", comparison);
    }

    [Fact]
    public void TryParse_WhenKeywordsEquals_ShouldGenerateAnyComparison()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = CreateBasicFilter(FilterCategory.Keywords, FilterEvaluator.Equals, "Audit Success");

        // Act
        var result = filterService.TryParse(source, out var comparison);

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
        var source = CreateBasicFilter(
            FilterCategory.Level,
            FilterEvaluator.MultiSelect,
            value: null,
            values: ["Error", "Warning"]);

        // Act
        var result = filterService.TryParse(source, out var comparison);

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
        var source = CreateBasicFilter(category, evaluator, value);

        // Act
        var result = filterService.TryParse(source, out var comparison);

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
        var source = CreateBasicFilter(category, evaluator, value);

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryParse_WhenQuotesInValue_ShouldEscapeQuotes()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = CreateBasicFilter(FilterCategory.Description, FilterEvaluator.Contains, "test\"value");

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("test\\\"value", comparison);
    }

    [Fact]
    public void TryParse_WhenSubFiltersExist_ShouldAppendSubFilterComparison()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = new BasicFilter(
            new FilterData { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = "100" },
            [
                new SubFilter(
                    new FilterData { Category = FilterCategory.Level, Evaluator = FilterEvaluator.Equals, Value = "Error" },
                    JoinWithAny: false)
            ]);

        // Act
        var result = filterService.TryParse(source, out var comparison);

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
        var source = new BasicFilter(
            new FilterData { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = "100" },
            [
                new SubFilter(
                    new FilterData { Category = FilterCategory.Level, Evaluator = FilterEvaluator.Equals, Value = "Error" },
                    JoinWithAny: true)
            ]);

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains(" || ", comparison);
    }

    [Fact]
    public void TryParse_WhenSubFilterWithoutCompareAny_ShouldUseAndOperator()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = new BasicFilter(
            new FilterData { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = "100" },
            [
                new SubFilter(
                    new FilterData { Category = FilterCategory.Level, Evaluator = FilterEvaluator.Equals, Value = "Error" },
                    JoinWithAny: false)
            ]);

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains(" && ", comparison);
    }

    [Fact]
    public void TryParse_WhenUserIdEquals_ShouldIncludeNullCheck()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = CreateBasicFilter(FilterCategory.UserId, FilterEvaluator.Equals, "S-1-5-21");

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("UserId != null", comparison);
    }

    [Theory]
    [InlineData(FilterCategory.Description, FilterEvaluator.Contains, "He said \"hi\".")]
    [InlineData(FilterCategory.Description, FilterEvaluator.Contains, @"path\to\file")]
    [InlineData(FilterCategory.Description, FilterEvaluator.Contains, "line one\r\nline two")]
    [InlineData(FilterCategory.Description, FilterEvaluator.Equals, "She wrote: \"yes\\no\".")]
    [InlineData(FilterCategory.Source, FilterEvaluator.Equals, "Source\"With\"Quotes")]
    public void TryParse_WhenValueHasSpecialCharacters_GeneratesParsableExpressionThatRoundTrips(
        FilterCategory category,
        FilterEvaluator evaluator,
        string rawValue)
    {
        var filterService = CreateFilterService();
        var source = CreateBasicFilter(category, evaluator, rawValue);

        var result = filterService.TryParse(source, out var comparison);

        Assert.True(result);

        // Round-trip through the actual Dynamic LINQ parser by compiling the produced expression.
        // If the escape syntax is wrong, TryCompile will return false.
        Assert.True(FilterCompiler.TryCompile(comparison, out var compiled, out _));

        var matchingEvent = category switch
        {
            FilterCategory.Description => EventUtils.CreateTestEvent(description: rawValue),
            FilterCategory.Source => EventUtils.CreateTestEvent(source: rawValue),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

        Assert.NotNull(compiled);
        Assert.True(compiled.Predicate(matchingEvent));
    }

    [Fact]
    public void TryParse_WithBasicFilter_WhenMainInvalid_ShouldReturnFalseEvenWithValidSubFilters()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = new BasicFilter(
            new FilterData { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = string.Empty },
            [
                new SubFilter(
                    new FilterData { Category = FilterCategory.Level, Evaluator = FilterEvaluator.Equals, Value = "Error" },
                    JoinWithAny: true)
            ]);

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.False(result);
        Assert.Empty(comparison);
    }

    [Fact]
    public void TryParse_WithBasicFilter_WhenMainOnly_ShouldMatchFilterModelOutput()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = new BasicFilter(
            new FilterData { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = "100" },
            []);

        var sourceResult = filterService.TryParse(source, out var sourceComparison);

        // Assert
        Assert.True(sourceResult);
        Assert.Equal("Id == \"100\"", sourceComparison);
    }

    [Fact]
    public void TryParse_WithBasicFilter_WhenMultiSelectKeywords_ShouldEmitAnyContainsExpression()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = new BasicFilter(
            new FilterData
            {
                Category = FilterCategory.Keywords,
                Evaluator = FilterEvaluator.MultiSelect,
                Values = ["Audit Success", "Audit Failure"]
            },
            []);

        // Act
        var sourceResult = filterService.TryParse(source, out var sourceComparison);

        // Assert
        Assert.True(sourceResult);
        Assert.Equal(
            "Keywords.Any(e => (new[] {\"Audit Success\", \"Audit Failure\"}).Contains(e))",
            sourceComparison);
    }

    [Fact]
    public void TryParse_WithBasicFilter_WhenMultiSelectNonKeywords_ShouldEmitContainsToStringExpression()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = new BasicFilter(
            new FilterData
            {
                Category = FilterCategory.Level,
                Evaluator = FilterEvaluator.MultiSelect,
                Values = ["Error", "Warning"]
            },
            []);

        // Act
        var sourceResult = filterService.TryParse(source, out var sourceComparison);

        // Assert
        Assert.True(sourceResult);
        Assert.Equal(
            "(new[] {\"Error\", \"Warning\"}).Contains(Level.ToString())",
            sourceComparison);
    }

    [Fact]
    public void TryParse_WithBasicFilter_WhenSourceIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var filterService = CreateFilterService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => filterService.TryParse((BasicFilter)null!, out _));
    }

    [Fact]
    public void TryParse_WithBasicFilter_WhenSubFilterInvalid_ShouldSkipWithoutOrphanedJoinOperator()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = new BasicFilter(
            new FilterData { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = "100" },
            [
                new SubFilter(
                    new FilterData { Category = FilterCategory.Level, Evaluator = FilterEvaluator.Equals, Value = "   " },
                    JoinWithAny: true),
                new SubFilter(
                    new FilterData { Category = FilterCategory.Source, Evaluator = FilterEvaluator.Equals, Value = "Kernel" },
                    JoinWithAny: false)
            ]);

        var expected = "Id == \"100\" && Source == \"Kernel\"" + Environment.NewLine;

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, comparison);
        Assert.DoesNotContain(" || ", comparison);
    }

    [Fact]
    public void TryParse_WithBasicFilter_WhenSubFiltersPresent_ShouldUseExactJoinAndNewlineOrdering()
    {
        // Arrange
        var filterService = CreateFilterService();
        var source = new BasicFilter(
            new FilterData { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = "100" },
            [
                new SubFilter(
                    new FilterData { Category = FilterCategory.Level, Evaluator = FilterEvaluator.Equals, Value = "Error" },
                    JoinWithAny: true),
                new SubFilter(
                    new FilterData { Category = FilterCategory.Source, Evaluator = FilterEvaluator.Contains, Value = "Kernel" },
                    JoinWithAny: false)
            ]);

        var expected =
            "Id == \"100\" || Level == \"Error\"" + Environment.NewLine +
            " && Source.Contains(\"Kernel\", StringComparison.OrdinalIgnoreCase)" + Environment.NewLine;

        // Act
        var result = filterService.TryParse(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, comparison);
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
    public void TryParseExpression_WhenXmlExpression_ShouldReturnTrue()
    {
        // Arrange
        var filterService = CreateFilterService();

        // Act
        var result = filterService.TryParseExpression("Xml.Contains(\"test\", StringComparison.OrdinalIgnoreCase)",
            out var error);

        // Assert
        Assert.True(result);
        Assert.Empty(error);
    }

    private static BasicFilter CreateBasicFilter(
        FilterCategory category,
        FilterEvaluator evaluator,
        string? value,
        IEnumerable<string>? values = null) =>
        new(
            new FilterData
            {
                Category = category,
                Evaluator = evaluator,
                Value = value,
                Values = values?.ToImmutableList() ?? []
            },
            []);

    private static FilterService CreateFilterService() => new();
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests.Compilation;

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
    public void FilterActiveLogs_WhenDuplicateLogIds_ShouldThrowOnSequentialPath()
    {
        // Arrange — two logs sharing the same Id (record-copy preserves Id).
        // logs.Count == 2 + IsFilteringEnabled false routes through the sequential path.
        var filterService = CreateFilterService();
        var original = new EventLogData("Log1", LogPathType.Channel, [FilterEventBuilder.CreateTestEvent(100)]);

        var duplicate = original with
        {
            Name = "Log2",
            Events = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(200) }.AsReadOnly()
        };

        Assert.Equal(original.Id, duplicate.Id);

        var logData = new List<EventLogData> { original, duplicate };
        var filter = new Filter(null, []);

        // Act + Assert — Dictionary.Add throws on the duplicate key.
        Assert.Throws<ArgumentException>(() => filterService.FilterActiveLogs(logData, filter));
    }

    [Fact]
    public void FilterActiveLogs_WhenDuplicateLogIdsAndParallelPath_ShouldThrowOnDuplicate()
    {
        // Arrange — two 6k-event logs (>10k total) with shared Id, with filtering enabled.
        // This forces the parallel path; the post-Parallel.For filtered.Add still throws.
        var filterService = CreateFilterService();
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = baseTime.AddMinutes(3_000);
        var dateFilter = new DateFilter { After = cutoff, Before = baseTime.AddMinutes(20_000), IsEnabled = true };
        var filter = new Filter(dateFilter, []);

        var log1Events = Enumerable.Range(0, 6_000)
            .Select(i => FilterEventBuilder.CreateTestEvent(i, timeCreated: baseTime.AddMinutes(i), recordId: i))
            .ToList();

        var log2Events = Enumerable.Range(0, 6_000)
            .Select(i =>
                FilterEventBuilder.CreateTestEvent(i, timeCreated: baseTime.AddMinutes(i + 1_000), recordId: i + 6_000))
            .ToList();

        var original = new EventLogData("Log1", LogPathType.Channel, log1Events);
        var duplicate = original with { Name = "Log2", Events = log2Events };

        Assert.Equal(original.Id, duplicate.Id);

        var logData = new List<EventLogData> { original, duplicate };

        // Act + Assert
        Assert.Throws<ArgumentException>(() => filterService.FilterActiveLogs(logData, filter));
    }

    [Fact]
    public void FilterActiveLogs_WhenEmptyLogData_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var filterService = CreateFilterService();
        var filter = new Filter(null, []);

        // Act
        var result = filterService.FilterActiveLogs([], filter);

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

        var dateFilter = new DateFilter { After = cutoff, Before = baseTime.AddMinutes(20_000), IsEnabled = true };
        var filter = new Filter(dateFilter, []);

        var log1Events = Enumerable.Range(0, 6_000)
            .Select(i => FilterEventBuilder.CreateTestEvent(i, timeCreated: baseTime.AddMinutes(i), recordId: i))
            .ToList();

        var log2Events = Enumerable.Range(0, 6_000)
            .Select(i =>
                FilterEventBuilder.CreateTestEvent(i, timeCreated: baseTime.AddMinutes(i + 1_000), recordId: i + 6_000))
            .ToList();

        var logData = new List<EventLogData>
        {
            new("Log1", LogPathType.Channel, log1Events),
            new("Log2", LogPathType.Channel, log2Events)
        };

        var expectedLog1 = log1Events
            .Where(e => e.TimeCreated >= cutoff && e.TimeCreated <= baseTime.AddMinutes(20_000))
            .ToList();

        var expectedLog2 = log2Events
            .Where(e => e.TimeCreated >= cutoff && e.TimeCreated <= baseTime.AddMinutes(20_000))
            .ToList();

        // Act
        var result = filterService.FilterActiveLogs(logData, filter);

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

        var log1Events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, level: FilterTestConstants.EventLevelError),
            FilterEventBuilder.CreateTestEvent(200, level: FilterTestConstants.EventLevelInformation)
        };

        var log2Events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, level: FilterTestConstants.EventLevelError),
            FilterEventBuilder.CreateTestEvent(300, level: FilterTestConstants.EventLevelError)
        };

        var logData = new List<EventLogData>
        {
            new("Log1", LogPathType.Channel, log1Events),
            new("Log2", LogPathType.Channel, log2Events)
        };

        var filter = new Filter(null, []);

        // Act
        var result = filterService.FilterActiveLogs(logData, filter);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterActiveLogs_WhenMultipleLogsButFilteringDisabled_ShouldReturnAllEventsPerLog()
    {
        // Arrange — IsFilteringEnabled short-circuit must stay engaged even with many logs to
        // avoid spinning up parallel work for a no-op filter.
        var filterService = CreateFilterService();

        var log1Events = Enumerable.Range(0, 6_000).Select(i => FilterEventBuilder.CreateTestEvent(i)).ToList();
        var log2Events = Enumerable.Range(0, 6_000).Select(i => FilterEventBuilder.CreateTestEvent(i)).ToList();

        var logData = new List<EventLogData>
        {
            new("Log1", LogPathType.Channel, log1Events),
            new("Log2", LogPathType.Channel, log2Events)
        };

        var filter = new Filter(null, []);

        // Act
        var result = filterService.FilterActiveLogs(logData, filter);

        // Assert
        Assert.Equal(log1Events.Count, result[logData[0].Id].Count);
        Assert.Equal(log2Events.Count, result[logData[1].Id].Count);
    }

    [Fact]
    public void FilterActiveLogs_WhenNoFiltersEnabled_ShouldReturnAllEvents()
    {
        // Arrange
        var filterService = CreateFilterService();

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200)
        };

        var logData = new List<EventLogData>
        {
            new("TestLog", LogPathType.Channel, events)
        };

        var filter = new Filter(null, []);

        // Act
        var result = filterService.FilterActiveLogs(logData, filter);

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
            .Select(i => FilterEventBuilder.CreateTestEvent(
                i,
                timeCreated: baseTime.AddMinutes(i),
                recordId: i))
            .ToList();

        var cutoff = baseTime.AddMinutes(eventCount / 2);
        var dateFilter = new DateFilter { After = cutoff, Before = baseTime.AddMinutes(eventCount), IsEnabled = true };
        var filter = new Filter(dateFilter, []);

        // Act
        var result = filterService.GetFilteredEvents(events, filter);

        // Assert — should return only events at or after the cutoff
        var expectedCount =
            events.Count(e => e.TimeCreated >= cutoff && e.TimeCreated <= baseTime.AddMinutes(eventCount));

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

        var dateFilter = new DateFilter { After = cutoff, Before = baseTime.AddMinutes(200), IsEnabled = true };
        var filter = new Filter(dateFilter, []);

        // Small collection (sequential path)
        var smallEvents = Enumerable.Range(0, 100)
            .Select(i => FilterEventBuilder.CreateTestEvent(i, timeCreated: baseTime.AddMinutes(i), recordId: i))
            .ToList();

        // Large collection (PLINQ path) with the same first 100 events
        var largeEvents = Enumerable.Range(0, 15_000)
            .Select(i => FilterEventBuilder.CreateTestEvent(i, timeCreated: baseTime.AddMinutes(i), recordId: i))
            .ToList();

        // Act
        var smallResult = filterService.GetFilteredEvents(smallEvents, filter);
        var largeResult = filterService.GetFilteredEvents(largeEvents, filter);

        // Assert — small result should match the first 100 events' filtered subset
        var expectedSmallCount =
            smallEvents.Count(e => e.TimeCreated >= cutoff && e.TimeCreated <= baseTime.AddMinutes(200));

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

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200),
            FilterEventBuilder.CreateTestEvent(300)
        };

        var filter = new Filter(null, []);

        // Act
        var result = filterService.GetFilteredEvents(events, filter);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Theory]
    [InlineData(EventProperty.Description,
        ComparisonOperator.Contains,
        MatchMode.Single,
        "error",
        "Description.Contains(\"error\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.Source,
        ComparisonOperator.Contains,
        MatchMode.Single,
        "Test",
        "Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    public void TryFormat_WhenContainsOperator_ShouldGenerateCorrectComparison(
        EventProperty property,
        ComparisonOperator op,
        MatchMode card,
        string value,
        string expectedComparison)
    {
        // Arrange
        var source = CreateBasicFilter(property, op, card, value);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryFormat_WhenEmptyValueAndSingleMatchMode_ShouldReturnFalse()
    {
        // Arrange
        var source = CreateBasicFilter(
            EventProperty.Id,
            ComparisonOperator.Equals,
            MatchMode.Single,
            null);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.False(result);
        Assert.Empty(comparison);
    }

    [Fact]
    public void TryFormat_WhenEmptyValuesAndManyMatchMode_ShouldReturnFalse()
    {
        // Arrange
        var source = CreateBasicFilter(
            EventProperty.Id,
            ComparisonOperator.Equals,
            MatchMode.Many,
            null);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.False(result);
        Assert.Empty(comparison);
    }

    [Theory]
    [InlineData(EventProperty.Id, ComparisonOperator.Equals, MatchMode.Single, "100", "Id == 100")]
    [InlineData(EventProperty.Level, ComparisonOperator.Equals, MatchMode.Single, "Error", "Level == \"Error\"")]
    [InlineData(EventProperty.Source,
        ComparisonOperator.Equals,
        MatchMode.Single,
        "TestSource",
        "Source == \"TestSource\"")]
    public void TryFormat_WhenEqualsOperator_ShouldGenerateCorrectComparison(
        EventProperty property,
        ComparisonOperator op,
        MatchMode card,
        string value,
        string expectedComparison)
    {
        // Arrange
        var source = CreateBasicFilter(property, op, card, value);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryFormat_WhenFieldIsXml_ShouldReturnTrue()
    {
        // Arrange
        var source = CreateBasicFilter(
            EventProperty.Xml,
            ComparisonOperator.Contains,
            MatchMode.Single,
            "test");

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.NotEmpty(comparison);
    }

    [Fact]
    public void TryFormat_WhenIdContains_ShouldEmitBareContains()
    {
        // Arrange
        var source = CreateBasicFilter(
            EventProperty.Id,
            ComparisonOperator.Contains,
            MatchMode.Single,
            "10");

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal("Id.Contains(\"10\", StringComparison.OrdinalIgnoreCase)", comparison);
    }

    [Fact]
    public void TryFormat_WhenKeywordsEquals_ShouldGenerateAnyComparison()
    {
        // Arrange
        var source = CreateBasicFilter(
            EventProperty.Keywords,
            ComparisonOperator.Equals,
            MatchMode.Single,
            "Audit Success");

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("Keywords.Any", comparison);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", comparison);
    }

    [Fact]
    public void TryFormat_WhenMultiSelectWithValues_ShouldGenerateContainsComparison()
    {
        // Arrange
        var source = CreateBasicFilter(
            EventProperty.Level,
            ComparisonOperator.Equals,
            MatchMode.Many,
            null,
            ["Error", "Warning"]);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("Error", comparison);
        Assert.Contains("Warning", comparison);
        Assert.Contains("Contains", comparison);
    }

    [Theory]
    [InlineData(EventProperty.Description,
        ComparisonOperator.NotContains,
        MatchMode.Single,
        "error",
        "!Description.Contains(\"error\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.Source,
        ComparisonOperator.NotContains,
        MatchMode.Single,
        "Test",
        "!Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    public void TryFormat_WhenNotContainsOperator_ShouldGenerateCorrectComparison(
        EventProperty property,
        ComparisonOperator op,
        MatchMode card,
        string value,
        string expectedComparison)
    {
        // Arrange
        var source = CreateBasicFilter(property, op, card, value);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Theory]
    [InlineData(EventProperty.Id, ComparisonOperator.NotEqual, MatchMode.Single, "100", "Id != 100")]
    [InlineData(EventProperty.Level, ComparisonOperator.NotEqual, MatchMode.Single, "Error", "Level != \"Error\"")]
    public void TryFormat_WhenNotEqualOperator_ShouldGenerateCorrectComparison(
        EventProperty property,
        ComparisonOperator op,
        MatchMode card,
        string value,
        string expectedComparison)
    {
        // Arrange
        var source = CreateBasicFilter(property, op, card, value);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedComparison, comparison);
    }

    [Fact]
    public void TryFormat_WhenQuotesInValue_ShouldEscapeQuotes()
    {
        // Arrange
        var source = CreateBasicFilter(
            EventProperty.Description,
            ComparisonOperator.Contains,
            MatchMode.Single,
            "test\"value");

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("test\\\"value", comparison);
    }

    [Fact]
    public void TryFormat_WhenSubFiltersExist_ShouldAppendSubFilterComparison()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    false)
            ]);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("Id == 100", comparison);
        Assert.Contains("Level == \"Error\"", comparison);
    }

    [Fact]
    public void TryFormat_WhenSubFilterWithCompareAny_ShouldUseOrOperator()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    true)
            ]);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains(" || ", comparison);
    }

    [Fact]
    public void TryFormat_WhenSubFilterWithoutCompareAny_ShouldUseAndOperator()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    false)
            ]);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains(" && ", comparison);
    }

    [Fact]
    public void TryFormat_WhenUserIdEquals_ShouldIncludeNullCheck()
    {
        // Arrange
        var source = CreateBasicFilter(
            EventProperty.UserId,
            ComparisonOperator.Equals,
            MatchMode.Single,
            "S-1-5-21");

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Contains("UserId != null", comparison);
    }

    [Theory]
    [InlineData(EventProperty.Description, ComparisonOperator.Contains, MatchMode.Single, "He said \"hi\".")]
    [InlineData(EventProperty.Description, ComparisonOperator.Contains, MatchMode.Single, @"path\to\file")]
    [InlineData(EventProperty.Description, ComparisonOperator.Contains, MatchMode.Single, "line one\r\nline two")]
    [InlineData(EventProperty.Description, ComparisonOperator.Equals, MatchMode.Single, "She wrote: \"yes\\no\".")]
    [InlineData(EventProperty.Source, ComparisonOperator.Equals, MatchMode.Single, "Source\"With\"Quotes")]
    public void TryFormat_WhenValueHasSpecialCharacters_GeneratesParsableExpressionThatRoundTrips(
        EventProperty property,
        ComparisonOperator op,
        MatchMode card,
        string rawValue)
    {
        var source = CreateBasicFilter(property, op, card, rawValue);

        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        Assert.True(result);

        // Round-trip through the actual Dynamic LINQ parser by compiling the produced expression.
        // If the escape syntax is wrong, TryCompile will return false.
        Assert.True(FilterCompiler.TryCompile(comparison, out var compiled, out _));

        var matchingEvent = property switch
        {
            EventProperty.Description => FilterEventBuilder.CreateTestEvent(description: rawValue),
            EventProperty.Source => FilterEventBuilder.CreateTestEvent(source: rawValue),
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };

        Assert.NotNull(compiled);
        Assert.True(compiled.Predicate(matchingEvent));
    }

    [Fact]
    public void TryFormat_WithBasicFilter_WhenMainInvalid_ShouldReturnFalseEvenWithValidSubFilters()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = string.Empty
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    true)
            ]);

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.False(result);
        Assert.Empty(comparison);
    }

    [Fact]
    public void TryFormat_WithBasicFilter_WhenMainOnly_ShouldMatchFilterModelOutput()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        var sourceResult = BasicFilterFormatter.TryFormat(source, out var sourceComparison);

        // Assert
        Assert.True(sourceResult);
        Assert.Equal("Id == 100", sourceComparison);
    }

    [Fact]
    public void TryFormat_WithBasicFilter_WhenMultiSelectKeywords_ShouldEmitAnyContainsExpression()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Keywords,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = ["Audit Success", "Audit Failure"]
            },
            []);

        // Act
        var sourceResult = BasicFilterFormatter.TryFormat(source, out var sourceComparison);

        // Assert
        Assert.True(sourceResult);

        Assert.Equal(
            "Keywords.Any(e => (new[] {\"Audit Success\", \"Audit Failure\"}).Contains(e))",
            sourceComparison);
    }

    [Fact]
    public void TryFormat_WithBasicFilter_WhenMultiSelectNonKeywords_ShouldEmitBareMultiSelectContains()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = ["Error", "Warning"]
            },
            []);

        // Act
        var sourceResult = BasicFilterFormatter.TryFormat(source, out var sourceComparison);

        // Assert
        Assert.True(sourceResult);

        Assert.Equal(
            "(new[] {\"Error\", \"Warning\"}).Contains(Level)",
            sourceComparison);
    }

    [Fact]
    public void TryFormat_WithBasicFilter_WhenSourceIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => BasicFilterFormatter.TryFormat(null!, out _));
    }

    [Fact]
    public void TryFormat_WithBasicFilter_WhenSubFilterInvalid_ShouldSkipWithoutOrphanedJoinOperator()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "   "
                    },
                    true),
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Source,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Kernel"
                    },
                    false)
            ]);

        var expected = "Id == 100 && Source == \"Kernel\"";

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, comparison);
        Assert.DoesNotContain(" || ", comparison);
    }

    [Fact]
    public void TryFormat_WithBasicFilter_WhenSubFiltersPresent_ShouldUseExactJoinOrdering()
    {
        // Arrange
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    true),
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Source,
                        Operator = ComparisonOperator.Contains,
                        MatchMode = MatchMode.Single,
                        Value = "Kernel"
                    },
                    false)
            ]);

        var expected =
            "Id == 100 || Level == \"Error\"" +
            " && Source.Contains(\"Kernel\", StringComparison.OrdinalIgnoreCase)";

        // Act
        var result = BasicFilterFormatter.TryFormat(source, out var comparison);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, comparison);
    }

    private static BasicFilter CreateBasicFilter(
        EventProperty property,
        ComparisonOperator op,
        MatchMode card,
        string? value,
        IEnumerable<string>? values = null) =>
        new(
            new FilterComparison
            {
                Property = property,
                Operator = op,
                MatchMode = card,
                Value = value,
                Values = values?.ToImmutableList() ?? []
            },
            []);

    private static FilterService CreateFilterService() => new();
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Text.Json;

namespace EventLogExpert.UI.Tests.Filters;

public sealed class FilterModelTests
{
    [Fact]
    public void Empty_HasNoCompiledArtifact()
    {
        // Assert
        Assert.Equal(string.Empty, SavedFilter.Empty.ComparisonText);
        Assert.Null(SavedFilter.Empty.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_BasicShape_PreservesBasicFilter()
    {
        // Act
        var original = SavedFilter.TryCreate(Constants.FilterIdEquals100AndLevelError, mode: FilterMode.Basic);

        // Assert
        Assert.NotNull(original);
        Assert.NotNull(original.BasicFilter);
        Assert.Equal(FilterMode.Basic, original.Mode);

        // Act
        string json = JsonSerializer.Serialize(original);

        // Assert
        Assert.DoesNotContain("FilterType", json);
        Assert.Contains("\"Mode\":\"Basic\"", json);

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(json);

        // Assert
        Assert.NotNull(restored);

        Assert.Equal(FilterMode.Basic, restored.Mode);
        Assert.NotNull(restored.BasicFilter);
        Assert.Equal(EventProperty.Id, restored.BasicFilter.Comparison.Property);
        Assert.Equal("100", restored.BasicFilter.Comparison.Value);
        Assert.Single(restored.BasicFilter.SubFilters);
        Assert.False(restored.BasicFilter.SubFilters[0].JoinWithAny);
        Assert.Equal(EventProperty.Level, restored.BasicFilter.SubFilters[0].Comparison.Property);
        Assert.Equal("Error", restored.BasicFilter.SubFilters[0].Comparison.Value);
    }

    [Fact]
    public void JsonRoundTrip_CachedMode_PersistsAndRestoresMode()
    {
        // Act
        var original = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            color: HighlightColor.Yellow,
            mode: FilterMode.Cached);

        // Assert
        Assert.NotNull(original);
        Assert.Equal(FilterMode.Cached, original.Mode);
        Assert.Null(original.BasicFilter);

        // Act
        string json = JsonSerializer.Serialize(original);

        // Assert
        Assert.Contains("\"Mode\":\"Cached\"", json);

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(json);

        // Assert
        Assert.NotNull(restored);

        Assert.Equal(FilterMode.Cached, restored.Mode);
        Assert.Null(restored.BasicFilter);
        Assert.Equal(Constants.FilterIdEquals100, restored.ComparisonText);
    }

    [Fact]
    public void JsonRoundTrip_InvalidExpression_DisablesFilterButRetainsText()
    {
        // Arrange
        const string BrokenJson =
            """
            { "Color": 0, "ComparisonText": "Id ===== ###", "IsExcluded": false }
            """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(BrokenJson);

        // Assert
        Assert.NotNull(restored);

        Assert.Equal("Id ===== ###", restored.ComparisonText);
        Assert.Null(restored.Compiled);
        Assert.False(restored.IsEnabled);
    }

    [Fact]
    public void JsonRoundTrip_LegacyAdvancedWithBasicFilter_DropsStaleBasicFilter()
    {
        // Arrange
        const string StaleSource =
            $$"""
            {
              "Color": 0,
              "ComparisonText": "{{Constants.FilterIdEquals100}}",
              "IsExcluded": false,
              "FilterType": "Advanced",
              "BasicFilter": { "Comparison": { "Category": 0, "Evaluator": 0, "Value": "100", "Values": [] }, "SubFilters": [] }
            }
            """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(StaleSource);

        // Assert
        Assert.NotNull(restored);

        Assert.Null(restored.BasicFilter);
    }

    [Fact]
    public void JsonRoundTrip_LegacyBasicMissingBlob_RepairsViaFreshDecompose()
    {
        // Arrange
        const string BrokenBasic =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false, "FilterType": "Basic" }
            """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(BrokenBasic);

        // Assert
        Assert.NotNull(restored);

        Assert.Equal(Constants.FilterIdEquals100, restored.ComparisonText);
        Assert.NotNull(restored.BasicFilter);
        Assert.Equal(EventProperty.Id, restored.BasicFilter.Comparison.Property);
        Assert.Equal("100", restored.BasicFilter.Comparison.Value);
    }

    [Fact]
    public void JsonRoundTrip_LegacyComparisonNullValue_DoesNotThrow()
    {
        // Arrange
        const string LegacyNullValue = """{ "Color": 0, "Comparison": { "Value": null }, "IsExcluded": false }""";

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(LegacyNullValue);

        // Assert
        Assert.NotNull(restored);

        Assert.Equal(string.Empty, restored.ComparisonText);
        Assert.Null(restored.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_LegacyShape_LoadsAsAdvancedAndCompiles()
    {
        // Arrange
        const string LegacyJson =
            $$"""
            { "Color": 2, "Comparison": { "Value": "{{Constants.FilterIdEquals100}}" }, "IsExcluded": true }
            """;

        // Act
        var model = JsonSerializer.Deserialize<SavedFilter>(LegacyJson);

        // Assert
        Assert.NotNull(model);

        Assert.Equal(Constants.FilterIdEquals100, model.ComparisonText);
        Assert.NotNull(model.Compiled);
        Assert.True(model.IsExcluded);
        Assert.True(model.IsEnabled);
        Assert.Null(model.BasicFilter);
    }

    [Fact]
    public void JsonRoundTrip_MissingComparisonText_DoesNotThrow()
    {
        // Arrange
        const string EmptyJson = """{ "Color": 0, "IsExcluded": false }""";

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(EmptyJson);

        // Assert
        Assert.NotNull(restored);

        Assert.Equal(string.Empty, restored.ComparisonText);
        Assert.Null(restored.Compiled);
        Assert.False(restored.IsEnabled);
    }

    [Fact]
    public void JsonRoundTrip_NewShape_BasicFilterAbsent_DecomposableText_PreservesNullIntent()
    {
        // Arrange
        const string AdvancedJson =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false, "Mode": "Advanced" }
            """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(AdvancedJson);

        // Assert
        Assert.NotNull(restored);

        Assert.Equal(Constants.FilterIdEquals100, restored.ComparisonText);
        Assert.NotNull(restored.Compiled);
        Assert.True(restored.IsEnabled);
        Assert.Null(restored.BasicFilter);
        Assert.Equal(FilterMode.Advanced, restored.Mode);
    }

    [Fact]
    public void JsonRoundTrip_NewShape_LegacyShapeWithoutMode_InfersFromBasicFilterPresence()
    {
        // Arrange
        const string LegacyAdvancedJson =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false }
            """;

        // Act
        var advanced = JsonSerializer.Deserialize<SavedFilter>(LegacyAdvancedJson);

        // Assert
        Assert.NotNull(advanced);
        Assert.Equal(FilterMode.Advanced, advanced.Mode);
        Assert.Null(advanced.BasicFilter);

        const string LegacyBasicJson =
            $$"""
            {
              "Color": 0,
              "ComparisonText": "{{Constants.FilterIdEquals100}}",
              "IsExcluded": false,
              "BasicFilter": { "Comparison": { "Property": 0, "Operator": 0, "MatchMode": 0, "Value": "100", "Values": [] }, "SubFilters": [] }
            }
            """;

        // Act
        var basicMode = JsonSerializer.Deserialize<SavedFilter>(LegacyBasicJson);

        // Assert
        Assert.NotNull(basicMode);
        Assert.Equal(FilterMode.Basic, basicMode.Mode);
        Assert.NotNull(basicMode.BasicFilter);
    }

    [Fact]
    public void JsonRoundTrip_NewShape_PersistsAndRestoresAllFields()
    {
        // Act
        var original = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            color: HighlightColor.Blue,
            isExcluded: true);

        // Assert
        Assert.NotNull(original);

        // Act
        string json = JsonSerializer.Serialize(original);

        // Assert
        Assert.DoesNotContain("FilterType", json);
        Assert.Contains("\"Mode\":\"Advanced\"", json);

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(json);

        // Assert
        Assert.NotNull(restored);

        Assert.Equal(original.ComparisonText, restored.ComparisonText);
        Assert.Equal(original.Color, restored.Color);
        Assert.Equal(original.IsExcluded, restored.IsExcluded);
        Assert.Equal(original.Mode, restored.Mode);
        Assert.NotNull(restored.Compiled);
    }

    [Fact]
    public void LoadFromPersisted_AdvancedMode_NullPersisted_PreservesNullIntent()
    {
        // Act
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            null,
            FilterMode.Advanced);

        // Assert
        Assert.NotNull(model.Compiled);
        Assert.True(model.IsEnabled);
        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Advanced, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_AdvancedMode_StalePersistedBasicFilter_ForcesNull()
    {
        // Arrange
        var stale = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        // Act
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            stale,
            FilterMode.Advanced);

        // Assert
        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Advanced, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_BasicMode_PersistedNonNullDecomposable_PrefersFresh()
    {
        // Arrange
        var stale = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "Error"
            },
            []);

        // Act
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            stale,
            FilterMode.Basic);

        // Assert
        Assert.NotNull(model.BasicFilter);
        Assert.NotSame(stale, model.BasicFilter);
        Assert.Equal(EventProperty.Id, model.BasicFilter.Comparison.Property);
        Assert.Equal("100", model.BasicFilter.Comparison.Value);
        Assert.Equal(FilterMode.Basic, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_BasicMode_PersistedNonNullNonDecomposable_FallsBackToPersisted()
    {
        // Arrange
        var stale = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "TestSource"
            },
            []);

        // Act
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterComputerNameEqualsServer01,
            HighlightColor.None,
            false,
            stale,
            FilterMode.Basic);

        // Assert
        Assert.NotNull(model.Compiled);
        Assert.True(model.IsEnabled);
        Assert.Same(stale, model.BasicFilter);
    }

    [Fact]
    public void LoadFromPersisted_BasicMode_PersistedNullDecomposableText_RecoversStructure()
    {
        // Act
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            null,
            FilterMode.Basic);

        // Assert
        Assert.NotNull(model.BasicFilter);
        Assert.Equal(EventProperty.Id, model.BasicFilter.Comparison.Property);
        Assert.Equal("100", model.BasicFilter.Comparison.Value);
        Assert.Equal(FilterMode.Basic, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_BasicMode_PersistedNullNonDecomposableText_PreservesText()
    {
        // Act
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterComputerNameEqualsServer01,
            HighlightColor.None,
            false,
            null,
            FilterMode.Basic);

        // Assert
        Assert.Null(model.BasicFilter);
        Assert.Equal(Constants.FilterComputerNameEqualsServer01, model.ComparisonText);
        Assert.Equal(FilterMode.Basic, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_CachedMode_StalePersistedBasicFilter_ForcesNull()
    {
        // Arrange
        var stale = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        // Act
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            stale,
            FilterMode.Cached);

        // Assert
        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Cached, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_CompileFailure_DisablesAndPreservesText()
    {
        // Act
        var model = SavedFilter.LoadFromPersisted(
            "Id ===== ###",
            HighlightColor.Blue,
            true,
            null,
            FilterMode.Basic);

        // Assert
        Assert.Equal("Id ===== ###", model.ComparisonText);
        Assert.Null(model.Compiled);
        Assert.False(model.IsEnabled);
        Assert.True(model.IsExcluded);
        Assert.Equal(HighlightColor.Blue, model.Color);
        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Basic, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_EmptyText_ReturnsPlaceholder()
    {
        // Act
        var model = SavedFilter.LoadFromPersisted(
            string.Empty,
            HighlightColor.None,
            false,
            null,
            FilterMode.Advanced);

        // Assert
        Assert.Equal(string.Empty, model.ComparisonText);
        Assert.Null(model.Compiled);
        Assert.False(model.IsEnabled);
        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Advanced, model.Mode);
    }

    [Fact]
    public void TryCreate_AdvancedMode_DecomposableText_DoesNotAutoDecompose()
    {
        // Act
        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100);

        // Assert
        Assert.NotNull(model);
        Assert.Equal(FilterMode.Advanced, model.Mode);
        Assert.Null(model.BasicFilter);
    }

    [Fact]
    public void TryCreate_BasicFilterSupplied_ForcesBasicMode()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        // Act
        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100, basicFilter, mode: FilterMode.Basic);

        // Assert
        Assert.NotNull(model);
        Assert.Equal(FilterMode.Basic, model.Mode);
        Assert.Same(basicFilter, model.BasicFilter);
    }

    [Theory]
    [InlineData(FilterMode.Advanced)]
    [InlineData(FilterMode.Cached)]
    public void TryCreate_BasicFilterSuppliedWithNonBasicMode_ThrowsArgumentException(FilterMode mode)
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        // Assert
        Assert.Throws<ArgumentException>(() =>
            SavedFilter.TryCreate(Constants.FilterIdEquals100, basicFilter, mode: mode));
    }

    [Fact]
    public void TryCreate_BasicMode_NoBasicFilterProvided_DecomposableText_AutoDecomposes()
    {
        // Act
        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100, mode: FilterMode.Basic);

        // Assert
        Assert.NotNull(model);
        Assert.Equal(FilterMode.Basic, model.Mode);
        Assert.NotNull(model.BasicFilter);
        Assert.Equal(EventProperty.Id, model.BasicFilter.Comparison.Property);
        Assert.Equal("100", model.BasicFilter.Comparison.Value);
    }

    [Fact]
    public void TryCreate_NoBasicFilterProvided_NonDecomposableText_BasicFilterStaysNull()
    {
        // Act
        var model = SavedFilter.TryCreate(Constants.FilterComputerNameEqualsServer01);

        // Assert
        Assert.NotNull(model);
        Assert.NotNull(model.Compiled);
        Assert.Null(model.BasicFilter);
    }

    [Fact]
    public void TryCreate_PreservesOptionalFields()
    {
        // Arrange
        var id = FilterId.Create();
        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        // Act
        var model = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            basicFilter,
            HighlightColor.Red,
            true,
            true,
            id,
            FilterMode.Basic);

        // Assert
        Assert.NotNull(model);
        Assert.Equal(id, model.Id);
        Assert.Equal(HighlightColor.Red, model.Color);
        Assert.Same(basicFilter, model.BasicFilter);
        Assert.True(model.IsEnabled);
        Assert.True(model.IsExcluded);
    }

    [Fact]
    public void TryCreate_WhenExpressionCompiles_ReturnsModelWithCompiledArtifact()
    {
        // Act
        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100);

        // Assert
        Assert.NotNull(model);
        Assert.Equal(Constants.FilterIdEquals100, model.ComparisonText);
        Assert.NotNull(model.Compiled);
        Assert.False(model.Compiled.RequiresXml);
    }

    [Fact]
    public void TryCreate_WhenExpressionFailsToCompile_ReturnsNull()
    {
        // Act
        var model = SavedFilter.TryCreate("Id ===== ###");

        // Assert
        Assert.Null(model);
    }

    [Fact]
    public void TryCreate_WhenExpressionIsEmpty_ReturnsNull()
    {
        // Assert
        Assert.Null(SavedFilter.TryCreate(string.Empty));
        Assert.Null(SavedFilter.TryCreate("   "));
    }
}

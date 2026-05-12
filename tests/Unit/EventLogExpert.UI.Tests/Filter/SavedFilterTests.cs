// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Text.Json;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class FilterModelTests
{
    [Fact]
    public void Empty_HasNoCompiledArtifact()
    {
        Assert.Equal(string.Empty, SavedFilter.Empty.ComparisonText);
        Assert.Null(SavedFilter.Empty.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_AdvancedWithBasicFilter_DropsStaleBasicFilter()
    {
        const string staleSource =
            $$"""
            {
              "Color": 0,
              "ComparisonText": "{{Constants.FilterIdEquals100}}",
              "IsExcluded": false,
              "FilterType": "Advanced",
              "BasicFilter": { "Comparison": { "Category": 0, "Evaluator": 0, "Value": "100", "Values": [] }, "SubFilters": [] }
            }
            """;

        var restored = JsonSerializer.Deserialize<SavedFilter>(staleSource);
        Assert.NotNull(restored);

        Assert.Equal(FilterType.Advanced, restored.FilterType);
        Assert.Null(restored.BasicFilter);
    }

    [Fact]
    public void JsonRoundTrip_BasicShape_PreservesBasicFilter()
    {
        var basicFilter = new BasicFilter(
            new FilterCondition
            {
                Category = FilterCategory.Level,
                Evaluator = FilterEvaluator.Equals,
                Value = "Error"
            },
            [
                new SubFilter(
                    new FilterCondition
                    {
                        Category = FilterCategory.Source,
                        Evaluator = FilterEvaluator.Contains,
                        Value = "Kernel"
                    },
                    JoinWithAny: true)
            ]);

        var original = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            FilterType.Basic,
            basicFilter);
        Assert.NotNull(original);

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SavedFilter>(json);
        Assert.NotNull(restored);

        Assert.Equal(FilterType.Basic, restored.FilterType);
        Assert.NotNull(restored.BasicFilter);
        Assert.Equal(FilterCategory.Level, restored.BasicFilter.Comparison.Category);
        Assert.Equal("Error", restored.BasicFilter.Comparison.Value);
        Assert.Single(restored.BasicFilter.SubFilters);
        Assert.True(restored.BasicFilter.SubFilters[0].JoinWithAny);
        Assert.Equal(FilterCategory.Source, restored.BasicFilter.SubFilters[0].Data.Category);
    }

    [Fact]
    public void JsonRoundTrip_BasicWithoutBasicFilter_DegradesToAdvanced()
    {
        const string brokenBasic =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false, "FilterType": "Basic" }
            """;

        var restored = JsonSerializer.Deserialize<SavedFilter>(brokenBasic);
        Assert.NotNull(restored);

        Assert.Equal(FilterType.Advanced, restored.FilterType);
        Assert.Null(restored.BasicFilter);
        Assert.NotNull(restored.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_InvalidExpression_DisablesFilterButRetainsText()
    {
        const string brokenJson =
            """
            { "Color": 0, "ComparisonText": "Id ===== ###", "IsExcluded": false, "FilterType": "Advanced" }
            """;

        var restored = JsonSerializer.Deserialize<SavedFilter>(brokenJson);
        Assert.NotNull(restored);

        Assert.Equal("Id ===== ###", restored.ComparisonText);
        Assert.Null(restored.Compiled);
        Assert.False(restored.IsEnabled);
    }

    [Fact]
    public void JsonRoundTrip_LegacyComparisonNullValue_DoesNotThrow()
    {
        const string legacyNullValue = """{ "Color": 0, "Comparison": { "Value": null }, "IsExcluded": false }""";

        var restored = JsonSerializer.Deserialize<SavedFilter>(legacyNullValue);
        Assert.NotNull(restored);

        Assert.Equal(string.Empty, restored.ComparisonText);
        Assert.Null(restored.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_LegacyShape_LoadsAsAdvancedAndCompiles()
    {
        // Legacy persistence format (pre-13d): "Comparison": { "Value": "..." }, no FilterType, no BasicFilter.
        const string legacyJson =
            $$"""
            { "Color": 2, "Comparison": { "Value": "{{Constants.FilterIdEquals100}}" }, "IsExcluded": true }
            """;

        var model = JsonSerializer.Deserialize<SavedFilter>(legacyJson);
        Assert.NotNull(model);

        Assert.Equal(Constants.FilterIdEquals100, model.ComparisonText);
        Assert.NotNull(model.Compiled);
        Assert.True(model.IsExcluded);
        Assert.True(model.IsEnabled);
        Assert.Equal(FilterType.Advanced, model.FilterType);
        Assert.Null(model.BasicFilter);
    }

    [Fact]
    public void JsonRoundTrip_MissingComparisonText_DoesNotThrow()
    {
        const string emptyJson = """{ "Color": 0, "IsExcluded": false }""";

        var restored = JsonSerializer.Deserialize<SavedFilter>(emptyJson);
        Assert.NotNull(restored);

        Assert.Equal(string.Empty, restored.ComparisonText);
        Assert.Null(restored.Compiled);
        Assert.False(restored.IsEnabled);
    }

    [Fact]
    public void JsonRoundTrip_NewShape_PersistsAndRestoresAllFields()
    {
        var original = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            FilterType.Cached,
            color: HighlightColor.Blue,
            isExcluded: true);
        Assert.NotNull(original);

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SavedFilter>(json);
        Assert.NotNull(restored);

        Assert.Equal(original.ComparisonText, restored.ComparisonText);
        Assert.Equal(original.Color, restored.Color);
        Assert.Equal(original.FilterType, restored.FilterType);
        Assert.Equal(original.IsExcluded, restored.IsExcluded);
        Assert.NotNull(restored.Compiled);
    }

    [Fact]
    public void TryCreate_PreservesOptionalFields()
    {
        var id = FilterId.Create();
        var basicFilter = new BasicFilter(
            new FilterCondition { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = "100" },
            []);

        var model = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            FilterType.Basic,
            basicFilter,
            HighlightColor.Red,
            isExcluded: true,
            isEnabled: true,
            id);

        Assert.NotNull(model);
        Assert.Equal(id, model.Id);
        Assert.Equal(HighlightColor.Red, model.Color);
        Assert.Equal(FilterType.Basic, model.FilterType);
        Assert.Same(basicFilter, model.BasicFilter);
        Assert.True(model.IsEnabled);
        Assert.True(model.IsExcluded);
    }

    [Fact]
    public void TryCreate_WhenExpressionCompiles_ReturnsModelWithCompiledArtifact()
    {
        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100);

        Assert.NotNull(model);
        Assert.Equal(Constants.FilterIdEquals100, model.ComparisonText);
        Assert.NotNull(model.Compiled);
        Assert.False(model.Compiled.RequiresXml);
    }

    [Fact]
    public void TryCreate_WhenExpressionFailsToCompile_ReturnsNull()
    {
        var model = SavedFilter.TryCreate("Id ===== ###");

        Assert.Null(model);
    }

    [Fact]
    public void TryCreate_WhenExpressionIsEmpty_ReturnsNull()
    {
        // FilterCompiler rejects empty/whitespace-only expressions.
        Assert.Null(SavedFilter.TryCreate(string.Empty));
        Assert.Null(SavedFilter.TryCreate("   "));
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Text.Json;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterModelTests
{
    [Fact]
    public void TryCreate_WhenExpressionCompiles_ReturnsModelWithCompiledArtifact()
    {
        var model = FilterModel.TryCreate(Constants.FilterIdEquals100);

        Assert.NotNull(model);
        Assert.Equal(Constants.FilterIdEquals100, model.ComparisonText);
        Assert.NotNull(model.Compiled);
        Assert.False(model.Compiled.RequiresXml);
    }

    [Fact]
    public void TryCreate_WhenExpressionFailsToCompile_ReturnsNull()
    {
        var model = FilterModel.TryCreate("Id ===== ###");

        Assert.Null(model);
    }

    [Fact]
    public void TryCreate_WhenExpressionIsEmpty_ReturnsNull()
    {
        // FilterCompiler rejects empty/whitespace-only expressions.
        Assert.Null(FilterModel.TryCreate(string.Empty));
        Assert.Null(FilterModel.TryCreate("   "));
    }

    [Fact]
    public void TryCreate_PreservesOptionalFields()
    {
        var id = FilterId.Create();
        var basicSource = new BasicFilterSource(
            new BasicFilterCriteria { Category = FilterCategory.Id, Evaluator = FilterEvaluator.Equals, Value = "100" },
            []);

        var model = FilterModel.TryCreate(
            Constants.FilterIdEquals100,
            FilterType.Basic,
            basicSource,
            HighlightColor.Red,
            isExcluded: true,
            isEnabled: true,
            id);

        Assert.NotNull(model);
        Assert.Equal(id, model.Id);
        Assert.Equal(HighlightColor.Red, model.Color);
        Assert.Equal(FilterType.Basic, model.FilterType);
        Assert.Same(basicSource, model.BasicSource);
        Assert.True(model.IsEnabled);
        Assert.True(model.IsExcluded);
    }

    [Fact]
    public void Empty_HasNoCompiledArtifact()
    {
        Assert.Equal(string.Empty, FilterModel.Empty.ComparisonText);
        Assert.Null(FilterModel.Empty.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_LegacyShape_LoadsAsAdvancedAndCompiles()
    {
        // Legacy persistence format (pre-13d): "Comparison": { "Value": "..." }, no FilterType, no BasicSource.
        const string legacyJson =
            $$"""
            { "Color": 2, "Comparison": { "Value": "{{Constants.FilterIdEquals100}}" }, "IsExcluded": true }
            """;

        var model = JsonSerializer.Deserialize<FilterModel>(legacyJson)!;

        Assert.Equal(Constants.FilterIdEquals100, model.ComparisonText);
        Assert.NotNull(model.Compiled);
        Assert.True(model.IsExcluded);
        Assert.True(model.IsEnabled);
        Assert.Equal(FilterType.Advanced, model.FilterType);
        Assert.Null(model.BasicSource);
    }

    [Fact]
    public void JsonRoundTrip_NewShape_PersistsAndRestoresAllFields()
    {
        var original = FilterModel.TryCreate(
            Constants.FilterIdEquals100,
            FilterType.Cached,
            color: HighlightColor.Blue,
            isExcluded: true)!;

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FilterModel>(json)!;

        Assert.Equal(original.ComparisonText, restored.ComparisonText);
        Assert.Equal(original.Color, restored.Color);
        Assert.Equal(original.FilterType, restored.FilterType);
        Assert.Equal(original.IsExcluded, restored.IsExcluded);
        Assert.NotNull(restored.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_BasicShape_PreservesBasicSource()
    {
        var basicSource = new BasicFilterSource(
            new BasicFilterCriteria
            {
                Category = FilterCategory.Level,
                Evaluator = FilterEvaluator.Equals,
                Value = "Error"
            },
            [
                new BasicSubClause(
                    new BasicFilterCriteria
                    {
                        Category = FilterCategory.Source,
                        Evaluator = FilterEvaluator.Contains,
                        Value = "Kernel"
                    },
                    JoinWithAny: true)
            ]);

        var original = FilterModel.TryCreate(
            Constants.FilterIdEquals100,
            FilterType.Basic,
            basicSource)!;

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FilterModel>(json)!;

        Assert.Equal(FilterType.Basic, restored.FilterType);
        Assert.NotNull(restored.BasicSource);
        Assert.Equal(FilterCategory.Level, restored.BasicSource.Main.Category);
        Assert.Equal("Error", restored.BasicSource.Main.Value);
        Assert.Single(restored.BasicSource.SubClauses);
        Assert.True(restored.BasicSource.SubClauses[0].JoinWithAny);
        Assert.Equal(FilterCategory.Source, restored.BasicSource.SubClauses[0].Criteria.Category);
    }

    [Fact]
    public void JsonRoundTrip_BasicWithoutSource_DegradesToAdvanced()
    {
        const string brokenBasic =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false, "FilterType": "Basic" }
            """;

        var restored = JsonSerializer.Deserialize<FilterModel>(brokenBasic)!;

        Assert.Equal(FilterType.Advanced, restored.FilterType);
        Assert.Null(restored.BasicSource);
        Assert.NotNull(restored.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_AdvancedWithBasicSource_DropsStaleBasicSource()
    {
        const string staleSource =
            $$"""
            {
              "Color": 0,
              "ComparisonText": "{{Constants.FilterIdEquals100}}",
              "IsExcluded": false,
              "FilterType": "Advanced",
              "BasicSource": { "Main": { "Category": 0, "Evaluator": 0, "Value": "100", "Values": [] }, "SubClauses": [] }
            }
            """;

        var restored = JsonSerializer.Deserialize<FilterModel>(staleSource)!;

        Assert.Equal(FilterType.Advanced, restored.FilterType);
        Assert.Null(restored.BasicSource);
    }

    [Fact]
    public void JsonRoundTrip_InvalidExpression_DisablesFilterButRetainsText()
    {
        const string brokenJson =
            """
            { "Color": 0, "ComparisonText": "Id ===== ###", "IsExcluded": false, "FilterType": "Advanced" }
            """;

        var restored = JsonSerializer.Deserialize<FilterModel>(brokenJson)!;

        Assert.Equal("Id ===== ###", restored.ComparisonText);
        Assert.Null(restored.Compiled);
        Assert.False(restored.IsEnabled);
    }

    [Fact]
    public void JsonRoundTrip_MissingComparisonText_DoesNotThrow()
    {
        const string emptyJson = """{ "Color": 0, "IsExcluded": false }""";

        var restored = JsonSerializer.Deserialize<FilterModel>(emptyJson)!;

        Assert.Equal(string.Empty, restored.ComparisonText);
        Assert.Null(restored.Compiled);
        Assert.False(restored.IsEnabled);
    }

    [Fact]
    public void JsonRoundTrip_LegacyComparisonNullValue_DoesNotThrow()
    {
        const string legacyNullValue = """{ "Color": 0, "Comparison": { "Value": null }, "IsExcluded": false }""";

        var restored = JsonSerializer.Deserialize<FilterModel>(legacyNullValue)!;

        Assert.Equal(string.Empty, restored.ComparisonText);
        Assert.Null(restored.Compiled);
    }
}

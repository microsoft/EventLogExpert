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
    public void JsonRoundTrip_BasicShape_PreservesBasicFilter()
    {
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "Error"
            },
            [
                new SubFilter(
                    new BasicFilterCondition
                    {
                        Property = EventProperty.Source,
                        Operator = ComparisonOperator.Contains,
                        MatchMode = MatchMode.Single,
                        Value = "Kernel"
                    },
                    JoinWithAny: true)
            ]);

        var original = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            basicFilter: basicFilter);
        Assert.NotNull(original);

        string json = JsonSerializer.Serialize(original);
        Assert.DoesNotContain("FilterType", json);

        var restored = JsonSerializer.Deserialize<SavedFilter>(json);
        Assert.NotNull(restored);

        Assert.NotNull(restored.BasicFilter);
        Assert.Equal(EventProperty.Level, restored.BasicFilter.Comparison.Property);
        Assert.Equal("Error", restored.BasicFilter.Comparison.Value);
        Assert.Single(restored.BasicFilter.SubFilters);
        Assert.True(restored.BasicFilter.SubFilters[0].JoinWithAny);
        Assert.Equal(EventProperty.Source, restored.BasicFilter.SubFilters[0].Data.Property);
    }

    [Fact]
    public void JsonRoundTrip_InvalidExpression_DisablesFilterButRetainsText()
    {
        const string brokenJson =
            """
            { "Color": 0, "ComparisonText": "Id ===== ###", "IsExcluded": false }
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
        // Legacy persistence format (pre-13d): "Comparison": { "Value": "..." }, no BasicFilter.
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
    public void JsonRoundTrip_LegacyAdvancedWithBasicFilter_DropsStaleBasicFilter()
    {
        // Pre-L1 persistence: legacy "FilterType":"Advanced" entries with a stale BasicFilter blob.
        // The reader must drop that blob so the filter doesn't reappear as Basic on load.
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

        Assert.Null(restored.BasicFilter);
    }

    [Fact]
    public void JsonRoundTrip_LegacyBasicWithoutBasicFilter_KeepsRawText()
    {
        // Pre-L1 persistence: legacy "FilterType":"Basic" entries that lost the BasicFilter blob.
        // The reader must keep the raw ComparisonText so the user can still see/edit the expression.
        const string brokenBasic =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false, "FilterType": "Basic" }
            """;

        var restored = JsonSerializer.Deserialize<SavedFilter>(brokenBasic);
        Assert.NotNull(restored);

        Assert.Equal(Constants.FilterIdEquals100, restored.ComparisonText);
        Assert.Null(restored.BasicFilter);
    }

    [Fact]
    public void JsonRoundTrip_NewShape_PersistsAndRestoresAllFields()
    {
        var original = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            color: HighlightColor.Blue,
            isExcluded: true);
        Assert.NotNull(original);

        string json = JsonSerializer.Serialize(original);
        Assert.DoesNotContain("FilterType", json);

        var restored = JsonSerializer.Deserialize<SavedFilter>(json);
        Assert.NotNull(restored);

        Assert.Equal(original.ComparisonText, restored.ComparisonText);
        Assert.Equal(original.Color, restored.Color);
        Assert.Equal(original.IsExcluded, restored.IsExcluded);
        Assert.NotNull(restored.Compiled);
    }

    [Fact]
    public void TryCreate_PreservesOptionalFields()
    {
        var id = FilterId.Create();
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        var model = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            basicFilter: basicFilter,
            color: HighlightColor.Red,
            isExcluded: true,
            isEnabled: true,
            id: id);

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

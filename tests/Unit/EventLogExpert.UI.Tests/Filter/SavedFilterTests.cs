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
        // TryCreate auto-decomposes when no BasicFilter is supplied, so the round-trip must preserve the structured
        // shape derived from the text (primary + AND-joined sub-filter).
        var original = SavedFilter.TryCreate(Constants.FilterIdEquals100AndLevelError);
        Assert.NotNull(original);
        Assert.NotNull(original.BasicFilter);

        string json = JsonSerializer.Serialize(original);
        Assert.DoesNotContain("FilterType", json);

        var restored = JsonSerializer.Deserialize<SavedFilter>(json);
        Assert.NotNull(restored);

        Assert.NotNull(restored.BasicFilter);
        Assert.Equal(EventProperty.Id, restored.BasicFilter.Comparison.Property);
        Assert.Equal("100", restored.BasicFilter.Comparison.Value);
        Assert.Single(restored.BasicFilter.SubFilters);
        Assert.False(restored.BasicFilter.SubFilters[0].JoinWithAny);
        Assert.Equal(EventProperty.Level, restored.BasicFilter.SubFilters[0].Data.Property);
        Assert.Equal("Error", restored.BasicFilter.SubFilters[0].Data.Value);
    }

    [Fact]
    public void JsonRoundTrip_InvalidExpression_DisablesFilterButRetainsText()
    {
        const string BrokenJson =
            """
            { "Color": 0, "ComparisonText": "Id ===== ###", "IsExcluded": false }
            """;

        var restored = JsonSerializer.Deserialize<SavedFilter>(BrokenJson);
        Assert.NotNull(restored);

        Assert.Equal("Id ===== ###", restored.ComparisonText);
        Assert.Null(restored.Compiled);
        Assert.False(restored.IsEnabled);
    }

    [Fact]
    public void JsonRoundTrip_LegacyAdvancedWithBasicFilter_DropsStaleBasicFilter()
    {
        // Pre-L1 persistence: legacy "FilterType":"Advanced" entries with a stale BasicFilter blob.
        // The reader must drop that blob so the filter doesn't reappear as Basic on load.
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

        var restored = JsonSerializer.Deserialize<SavedFilter>(StaleSource);
        Assert.NotNull(restored);

        Assert.Null(restored.BasicFilter);
    }

    [Fact]
    public void JsonRoundTrip_LegacyBasicMissingBlob_RepairsViaFreshDecompose()
    {
        // Pre-L1 persistence: legacy "FilterType":"Basic" entries that lost the BasicFilter blob.
        // The reader recovers the structured form by running BasicFilterDecomposer against the persisted text
        // so the filter reopens as Basic on next edit instead of forcing the user to re-author it.
        const string BrokenBasic =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false, "FilterType": "Basic" }
            """;

        var restored = JsonSerializer.Deserialize<SavedFilter>(BrokenBasic);
        Assert.NotNull(restored);

        Assert.Equal(Constants.FilterIdEquals100, restored.ComparisonText);
        Assert.NotNull(restored.BasicFilter);
        Assert.Equal(EventProperty.Id, restored.BasicFilter.Comparison.Property);
        Assert.Equal("100", restored.BasicFilter.Comparison.Value);
    }

    [Fact]
    public void JsonRoundTrip_LegacyComparisonNullValue_DoesNotThrow()
    {
        const string LegacyNullValue = """{ "Color": 0, "Comparison": { "Value": null }, "IsExcluded": false }""";

        var restored = JsonSerializer.Deserialize<SavedFilter>(LegacyNullValue);
        Assert.NotNull(restored);

        Assert.Equal(string.Empty, restored.ComparisonText);
        Assert.Null(restored.Compiled);
    }

    [Fact]
    public void JsonRoundTrip_LegacyShape_LoadsAsAdvancedAndCompiles()
    {
        // Legacy persistence format (pre-13d): "Comparison": { "Value": "..." }, no BasicFilter.
        const string LegacyJson =
            $$"""
            { "Color": 2, "Comparison": { "Value": "{{Constants.FilterIdEquals100}}" }, "IsExcluded": true }
            """;

        var model = JsonSerializer.Deserialize<SavedFilter>(LegacyJson);
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
        const string EmptyJson = """{ "Color": 0, "IsExcluded": false }""";

        var restored = JsonSerializer.Deserialize<SavedFilter>(EmptyJson);
        Assert.NotNull(restored);

        Assert.Equal(string.Empty, restored.ComparisonText);
        Assert.Null(restored.Compiled);
        Assert.False(restored.IsEnabled);
    }

    [Fact]
    public void JsonRoundTrip_NewShape_BasicFilterAbsent_DecomposableText_PreservesNullIntent()
    {
        // Reproduces the Advanced-row save-then-reload path: the user typed a Basic-vocabulary expression in the
        // Advanced row, so EditableFilterRowBase.TrySaveAsync persisted it without a BasicFilter blob. The reader
        // must NOT auto-decompose on reload, otherwise the row silently flips from Advanced to Basic.
        const string AdvancedJson =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false }
            """;

        var restored = JsonSerializer.Deserialize<SavedFilter>(AdvancedJson);
        Assert.NotNull(restored);

        Assert.Equal(Constants.FilterIdEquals100, restored.ComparisonText);
        Assert.NotNull(restored.Compiled);
        Assert.True(restored.IsEnabled);
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
    public void LoadFromPersisted_CompileFailure_DisablesAndPreservesText()
    {
        var model = SavedFilter.LoadFromPersisted(
            "Id ===== ###",
            HighlightColor.Blue,
            true,
            null);

        Assert.Equal("Id ===== ###", model.ComparisonText);
        Assert.Null(model.Compiled);
        Assert.False(model.IsEnabled);
        Assert.True(model.IsExcluded);
        Assert.Equal(HighlightColor.Blue, model.Color);
        Assert.Null(model.BasicFilter);
    }

    [Fact]
    public void LoadFromPersisted_CompileSuccess_NullPersisted_PreservesNullIntent()
    {
        // Decomposable text + null persisted → MUST NOT auto-decompose. This is the regression for Advanced
        // filters whose text happens to map into the Basic vocabulary; reloading must keep them Advanced
        // (BasicFilter == null) so FilterPane doesn't silently re-render them as a structured row.
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            null);

        Assert.NotNull(model.Compiled);
        Assert.True(model.IsEnabled);
        Assert.Null(model.BasicFilter);
    }

    [Fact]
    public void LoadFromPersisted_CompileSuccess_PersistedNonNullDecomposable_PrefersFresh()
    {
        // Persisted carries a stale shape that doesn't match the text; LoadFromPersisted prefers the freshly
        // decomposed structure so the structured form stays consistent with what evaluates against events.
        var stale = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "Error"
            },
            []);

        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            stale);

        Assert.NotNull(model.BasicFilter);
        Assert.NotSame(stale, model.BasicFilter);
        Assert.Equal(EventProperty.Id, model.BasicFilter.Comparison.Property);
        Assert.Equal("100", model.BasicFilter.Comparison.Value);
    }

    [Fact]
    public void LoadFromPersisted_CompileSuccess_PersistedNonNullNonDecomposable_FallsBackToPersisted()
    {
        // Text is non-decomposable (ComputerName is outside the BasicFilter authoring vocabulary) but persisted
        // carries a structured shape — preserve persisted so a future vocabulary widening (or an earlier-version
        // decomposer that accepted the shape) doesn't lose the user's structure.
        var stale = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "TestSource"
            },
            []);

        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterComputerNameEqualsServer01,
            HighlightColor.None,
            false,
            stale);

        Assert.NotNull(model.Compiled);
        Assert.True(model.IsEnabled);
        Assert.Same(stale, model.BasicFilter);
    }

    [Fact]
    public void LoadFromPersisted_EmptyText_ReturnsPlaceholder()
    {
        var model = SavedFilter.LoadFromPersisted(
            string.Empty,
            HighlightColor.None,
            false,
            null);

        Assert.Equal(string.Empty, model.ComparisonText);
        Assert.Null(model.Compiled);
        Assert.False(model.IsEnabled);
        Assert.Null(model.BasicFilter);
    }

    [Fact]
    public void TryCreate_NoBasicFilterProvided_DecomposableText_AutoDecomposes()
    {
        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100);

        Assert.NotNull(model);
        Assert.NotNull(model.BasicFilter);
        Assert.Equal(EventProperty.Id, model.BasicFilter.Comparison.Property);
        Assert.Equal("100", model.BasicFilter.Comparison.Value);
    }

    [Fact]
    public void TryCreate_NoBasicFilterProvided_NonDecomposableText_BasicFilterStaysNull()
    {
        // ComputerName is outside the BasicFilter authoring vocabulary; the decomposer refuses, leaving the filter
        // as Advanced (BasicFilter == null) even though the expression compiles successfully.
        var model = SavedFilter.TryCreate(Constants.FilterComputerNameEqualsServer01);

        Assert.NotNull(model);
        Assert.NotNull(model.Compiled);
        Assert.Null(model.BasicFilter);
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
            basicFilter,
            HighlightColor.Red,
            true,
            true,
            id);

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

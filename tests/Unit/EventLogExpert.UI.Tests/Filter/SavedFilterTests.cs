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
        // TryCreate(mode: Basic) auto-decomposes when no BasicFilter is supplied, so the round-trip must
        // preserve the structured shape derived from the text (primary + AND-joined sub-filter).
        var original = SavedFilter.TryCreate(Constants.FilterIdEquals100AndLevelError, mode: FilterMode.Basic);
        Assert.NotNull(original);
        Assert.NotNull(original.BasicFilter);
        Assert.Equal(FilterMode.Basic, original.Mode);

        string json = JsonSerializer.Serialize(original);
        Assert.DoesNotContain("FilterType", json);
        Assert.Contains("\"Mode\":\"Basic\"", json);

        var restored = JsonSerializer.Deserialize<SavedFilter>(json);
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
        var original = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            color: HighlightColor.Yellow,
            mode: FilterMode.Cached);

        Assert.NotNull(original);
        Assert.Equal(FilterMode.Cached, original.Mode);
        Assert.Null(original.BasicFilter);

        string json = JsonSerializer.Serialize(original);
        Assert.Contains("\"Mode\":\"Cached\"", json);

        var restored = JsonSerializer.Deserialize<SavedFilter>(json);
        Assert.NotNull(restored);

        Assert.Equal(FilterMode.Cached, restored.Mode);
        Assert.Null(restored.BasicFilter);
        Assert.Equal(Constants.FilterIdEquals100, restored.ComparisonText);
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
        // Advanced row, so SaveFilter persisted Mode=Advanced (no BasicFilter blob). The reader must NOT
        // auto-decompose on reload, otherwise the row silently flips from Advanced to Basic.
        const string AdvancedJson =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false, "Mode": "Advanced" }
            """;

        var restored = JsonSerializer.Deserialize<SavedFilter>(AdvancedJson);
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
        // L1..L4a persistence shape (no Mode field, no FilterType): converter infers Mode from BasicFilter
        // presence so back-compat reads stay stable across the L4b cutover.
        const string LegacyAdvancedJson =
            $$"""
            { "Color": 0, "ComparisonText": "{{Constants.FilterIdEquals100}}", "IsExcluded": false }
            """;

        var advanced = JsonSerializer.Deserialize<SavedFilter>(LegacyAdvancedJson);
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

        var basicMode = JsonSerializer.Deserialize<SavedFilter>(LegacyBasicJson);
        Assert.NotNull(basicMode);
        Assert.Equal(FilterMode.Basic, basicMode.Mode);
        Assert.NotNull(basicMode.BasicFilter);
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
        Assert.Contains("\"Mode\":\"Advanced\"", json);

        var restored = JsonSerializer.Deserialize<SavedFilter>(json);
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
        // Decomposable text + Mode=Advanced + null persisted → MUST NOT auto-decompose. This is the regression
        // for Advanced filters whose text happens to map into the Basic vocabulary; reloading must keep them
        // Advanced (BasicFilter == null) so FilterPane doesn't silently re-render them as a structured row.
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            null,
            FilterMode.Advanced);

        Assert.NotNull(model.Compiled);
        Assert.True(model.IsEnabled);
        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Advanced, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_AdvancedMode_StalePersistedBasicFilter_ForcesNull()
    {
        // Mode wins: even when persisted carries a stale BasicFilter blob, Advanced/Cached modes force
        // BasicFilter to null on load so the row reopens on the correct surface.
        var stale = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            stale,
            FilterMode.Advanced);

        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Advanced, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_BasicMode_PersistedNonNullDecomposable_PrefersFresh()
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
            stale,
            FilterMode.Basic);

        Assert.NotNull(model.BasicFilter);
        Assert.NotSame(stale, model.BasicFilter);
        Assert.Equal(EventProperty.Id, model.BasicFilter.Comparison.Property);
        Assert.Equal("100", model.BasicFilter.Comparison.Value);
        Assert.Equal(FilterMode.Basic, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_BasicMode_PersistedNonNullNonDecomposable_FallsBackToPersisted()
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
            stale,
            FilterMode.Basic);

        Assert.NotNull(model.Compiled);
        Assert.True(model.IsEnabled);
        Assert.Same(stale, model.BasicFilter);
    }

    [Fact]
    public void LoadFromPersisted_BasicMode_PersistedNullDecomposableText_RecoversStructure()
    {
        // Hand-edited / partial-write JSON: Mode=Basic but BasicFilter blob missing. LoadFromPersisted runs the
        // decomposer against the persisted text so the row reopens on the Basic surface with structure populated
        // (rather than an empty Basic editor that hides the raw text and silently wipes it on next save).
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            null,
            FilterMode.Basic);

        Assert.NotNull(model.BasicFilter);
        Assert.Equal(EventProperty.Id, model.BasicFilter.Comparison.Property);
        Assert.Equal("100", model.BasicFilter.Comparison.Value);
        Assert.Equal(FilterMode.Basic, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_BasicMode_PersistedNullNonDecomposableText_PreservesText()
    {
        // Same shape as above but the text is non-decomposable. BasicFilter stays null; the raw ComparisonText is
        // preserved so the user can recover via mode-switch to Advanced (the FilterDraft mode-switch path
        // intentionally preserves the existing text for this degraded shape).
        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterComputerNameEqualsServer01,
            HighlightColor.None,
            false,
            null,
            FilterMode.Basic);

        Assert.Null(model.BasicFilter);
        Assert.Equal(Constants.FilterComputerNameEqualsServer01, model.ComparisonText);
        Assert.Equal(FilterMode.Basic, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_CachedMode_StalePersistedBasicFilter_ForcesNull()
    {
        var stale = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        var model = SavedFilter.LoadFromPersisted(
            Constants.FilterIdEquals100,
            HighlightColor.None,
            false,
            stale,
            FilterMode.Cached);

        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Cached, model.Mode);
    }

    [Fact]
    public void LoadFromPersisted_CompileFailure_DisablesAndPreservesText()
    {
        var model = SavedFilter.LoadFromPersisted(
            "Id ===== ###",
            HighlightColor.Blue,
            true,
            null,
            FilterMode.Basic);

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
        var model = SavedFilter.LoadFromPersisted(
            string.Empty,
            HighlightColor.None,
            false,
            null,
            FilterMode.Advanced);

        Assert.Equal(string.Empty, model.ComparisonText);
        Assert.Null(model.Compiled);
        Assert.False(model.IsEnabled);
        Assert.Null(model.BasicFilter);
        Assert.Equal(FilterMode.Advanced, model.Mode);
    }

    [Fact]
    public void TryCreate_AdvancedMode_DecomposableText_DoesNotAutoDecompose()
    {
        // Mode=Advanced (default) must not opportunistically populate BasicFilter even when the text would
        // decompose cleanly — the caller's mode intent is authoritative.
        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100);

        Assert.NotNull(model);
        Assert.Equal(FilterMode.Advanced, model.Mode);
        Assert.Null(model.BasicFilter);
    }

    [Fact]
    public void TryCreate_BasicFilterSupplied_ForcesBasicMode()
    {
        // Supplying basicFilter requires Basic intent; passing the matching mode round-trips the structure.
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100, basicFilter, mode: FilterMode.Basic);

        Assert.NotNull(model);
        Assert.Equal(FilterMode.Basic, model.Mode);
        Assert.Same(basicFilter, model.BasicFilter);
    }

    [Theory]
    [InlineData(FilterMode.Advanced)]
    [InlineData(FilterMode.Cached)]
    public void TryCreate_BasicFilterSuppliedWithNonBasicMode_ThrowsArgumentException(FilterMode mode)
    {
        // Fail-loud guard: callers asserting Basic intent (passing a BasicFilter) must also pass mode=Basic.
        // Prior to L4b polish the factory silently rewrote mode → Basic, which let mismatches survive review.
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        Assert.Throws<ArgumentException>(() =>
            SavedFilter.TryCreate(Constants.FilterIdEquals100, basicFilter, mode: mode));
    }

    [Fact]
    public void TryCreate_BasicMode_NoBasicFilterProvided_DecomposableText_AutoDecomposes()
    {
        var model = SavedFilter.TryCreate(Constants.FilterIdEquals100, mode: FilterMode.Basic);

        Assert.NotNull(model);
        Assert.Equal(FilterMode.Basic, model.Mode);
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
            id,
            FilterMode.Basic);

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

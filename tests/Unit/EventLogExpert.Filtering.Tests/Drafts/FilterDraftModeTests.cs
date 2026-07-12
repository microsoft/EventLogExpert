// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils.Constants;

namespace EventLogExpert.Filtering.Tests.Drafts;

public sealed class FilterDraftModeTests
{
    [Fact]
    public void ApplyModeSwitch_AdvancedToBasic_DecomposableText_HydratesStructure()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Advanced, ComparisonText = FilterTestConstants.FilterIdEquals100 };

        draft.ApplyModeSwitch(FilterMode.Basic);

        Assert.Equal(FilterMode.Basic, draft.Mode);
        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Equal("100", draft.Comparison.Value);
    }

    [Fact]
    public void ApplyModeSwitch_AdvancedToBasic_NonDecomposableText_ClearsBoth()
    {
        // Caller is responsible for prompting the user FIRST when WouldLoseDataSwitchingTo is true; this
        // branch covers the "user accepted loss" path. ApplyModeSwitch must not refuse.
        FilterDraft draft = new()
        {
            Mode = FilterMode.Advanced,
            ComparisonText = FilterTestConstants.FilterComputerNameEqualsServer01
        };

        draft.ApplyModeSwitch(FilterMode.Basic);

        Assert.Equal(FilterMode.Basic, draft.Mode);
        Assert.Equal(string.Empty, draft.ComparisonText);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.Predicates);
    }

    [Fact]
    public void ApplyModeSwitch_BasicToAdvanced_DegradedTextOnly_PreservesText()
    {
        // Degraded persisted-Basic shape: ComparisonText carries the only signal the row has, structure is
        // empty because the BasicFilter blob couldn't be recovered. Going Advanced must surface the raw text
        // so the user can repair it manually — blanking it would destroy the only data the row carried.
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            ComparisonText = FilterTestConstants.FilterComputerNameEqualsServer01
        };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal(FilterTestConstants.FilterComputerNameEqualsServer01, draft.ComparisonText);
    }

    [Fact]
    public void ApplyModeSwitch_BasicToAdvanced_EmptyStructureAndEmptyText_StaysEmpty()
    {
        // Brand-new empty Basic row going Advanced: nothing to preserve, nothing to wipe.
        FilterDraft draft = new() { Mode = FilterMode.Basic };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal(string.Empty, draft.ComparisonText);
    }

    [Fact]
    public void ApplyModeSwitch_BasicToAdvanced_FormattableStructure_PopulatesText()
    {
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            }
        };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal("Id == 100", draft.ComparisonText);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.Predicates);
    }

    [Fact]
    public void ApplyModeSwitch_BasicToAdvanced_IncompleteSubFilter_FallsBackToLenient()
    {
        // After user-confirmed loss, lenient formatter drops the incomplete predicate so the user gets
        // text representing the parts that were valid; an empty result clears the text outright.
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            }
        };

        draft.Predicates.Add(new FilterPredicateDraft
        {
            Comparison = new FilterComparisonDraft
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = string.Empty
            },
            JoinWithAny = false
        });

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.False(string.IsNullOrEmpty(draft.ComparisonText));
        Assert.Empty(draft.Predicates);
    }

    [Fact]
    public void ApplyModeSwitch_CachedToAdvanced_PreservesText()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Cached, ComparisonText = FilterTestConstants.FilterIdEquals100 };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal(FilterTestConstants.FilterIdEquals100, draft.ComparisonText);
    }

    [Fact]
    public void ApplyModeSwitch_CachedToBasic_DecomposableText_HydratesStructure()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Cached, ComparisonText = FilterTestConstants.FilterIdEquals100 };

        draft.ApplyModeSwitch(FilterMode.Basic);

        Assert.Equal(FilterMode.Basic, draft.Mode);
        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Equal("100", draft.Comparison.Value);
    }

    [Fact]
    public void ApplyModeSwitch_SameMode_IsNoOp()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Advanced, ComparisonText = "Id == 100" };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal("Id == 100", draft.ComparisonText);
    }

    [Fact]
    public void ApplyModeSwitch_ToCached_ClearsTextAndStructure()
    {
        FilterDraft draft = new()
        {
            Mode = FilterMode.Advanced,
            ComparisonText = "Id == 100",
            Comparison =
            {
                Value = "200"
            }
        };

        draft.Predicates.Add(new FilterPredicateDraft());

        draft.ApplyModeSwitch(FilterMode.Cached);

        Assert.Equal(FilterMode.Cached, draft.Mode);
        Assert.Equal(string.Empty, draft.ComparisonText);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.Predicates);
    }

    [Fact]
    public void HasMeaningfulStructure_ComparisonValueSet_IsTrue()
    {
        FilterDraft draft = new()
        {
            Comparison =
            {
                Value = "100"
            }
        };

        Assert.True(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_ComparisonValuesPopulated_IsTrue()
    {
        FilterDraft draft = new();
        draft.Comparison.Values.Add("Error");

        Assert.True(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_ContainsManyOnlyEmpty_IsFalse()
    {
        // A Contains-Many that carries only empty values is degenerate (would match everything) and formats to nothing,
        // so it does not count as meaningful structure.
        FilterDraft draft = new()
        {
            Comparison =
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Contains,
                MatchMode = MatchMode.Many,
                Values = ["", ""]
            }
        };

        Assert.False(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_DefaultDraft_IsFalse()
    {
        FilterDraft draft = new();

        Assert.False(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_EqualsManyEmpty_IsTrue()
    {
        // Equals with an empty value is a valid empty-valued-field match, so it carries meaningful data.
        FilterDraft draft = new()
        {
            Comparison =
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = [""]
            }
        };

        Assert.True(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_OnlyPropertyDefaulted_IsFalse()
    {
        // FilterComparisonDraft.Property defaults to the first enum value (the dropdown's initial selection).
        // The default selection is not "user input" — without a value, save validation must reject as empty.
        FilterDraft draft = new()
        {
            Comparison =
            {
                Property = EventProperty.Source
            }
        };

        Assert.False(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_SubFilterPresent_IsTrue()
    {
        FilterDraft draft = new();
        draft.Predicates.Add(new FilterPredicateDraft());

        Assert.True(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void TryBuildSavedFilter_AdvancedMode_CompileFailure_ReturnsCompileError()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Advanced, ComparisonText = "Id ===== ###" };

        bool ok = draft.TryBuildSavedFilter(out SavedFilter? saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_AdvancedMode_DecomposableText_ProducesAdvancedSavedFilterWithoutBasicFilter()
    {
        // L4b intent guard: even when the text decomposes cleanly, an Advanced-mode draft saves as Advanced.
        // The row reopens on the Advanced surface on next edit, no silent surface flip.
        FilterDraft draft = new()
            { Mode = FilterMode.Advanced, ComparisonText = FilterTestConstants.FilterIdEquals100 };

        bool ok = draft.TryBuildSavedFilter(out SavedFilter? saved, out string error);

        Assert.True(ok, error);
        Assert.NotNull(saved);
        Assert.Equal(FilterMode.Advanced, saved.Mode);
        Assert.Null(saved.BasicFilter);
    }

    [Fact]
    public void TryBuildSavedFilter_AdvancedMode_EmptyText_ReturnsError()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Advanced, ComparisonText = string.Empty };

        bool ok = draft.TryBuildSavedFilter(out SavedFilter? saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_BasicMode_EmptyStructure_ReturnsErrorMessage()
    {
        FilterDraft draft = new() { Mode = FilterMode.Basic };

        bool ok = draft.TryBuildSavedFilter(out SavedFilter? saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_BasicMode_IncompleteSubFilter_ReturnsError()
    {
        // The strict-predicates formatter overload exists specifically for this gate: an incomplete predicate
        // (e.g. property selected but value blank) must NOT silently disappear from the saved expression.
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            }
        };

        draft.Predicates.Add(new FilterPredicateDraft
        {
            Comparison = new FilterComparisonDraft
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = string.Empty
            },
            JoinWithAny = false
        });

        bool ok = draft.TryBuildSavedFilter(out SavedFilter? saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_BasicMode_StructuredInput_BuildsBasicSavedFilter()
    {
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            }
        };

        bool ok = draft.TryBuildSavedFilter(out SavedFilter? saved, out string error);

        Assert.True(ok, error);
        Assert.NotNull(saved);
        Assert.Equal(FilterMode.Basic, saved.Mode);
        Assert.NotNull(saved.BasicFilter);
        Assert.Equal("Id == 100", saved.ComparisonText);
    }

    [Fact]
    public void TryBuildSavedFilter_CachedMode_EmptyText_ReturnsError()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Cached, ComparisonText = string.Empty };

        bool ok = draft.TryBuildSavedFilter(out SavedFilter? saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_CachedMode_WithText_ProducesCachedSavedFilter()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Cached, ComparisonText = FilterTestConstants.FilterIdEquals100 };

        bool ok = draft.TryBuildSavedFilter(out SavedFilter? saved, out string error);

        Assert.True(ok, error);
        Assert.NotNull(saved);
        Assert.Equal(FilterMode.Cached, saved.Mode);
        Assert.Null(saved.BasicFilter);
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_AdvancedToBasic_DecomposableText_IsFalse()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Advanced, ComparisonText = FilterTestConstants.FilterIdEquals100 };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Basic));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_AdvancedToBasic_NonDecomposableText_IsTrue()
    {
        FilterDraft draft = new()
        {
            Mode = FilterMode.Advanced,
            ComparisonText = FilterTestConstants.FilterComputerNameEqualsServer01
        };

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Basic));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_AdvancedToCached_EmptyText_IsFalse()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Advanced, ComparisonText = string.Empty };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_AdvancedToCached_NonEmptyText_IsTrue()
    {
        FilterDraft draft = new()
            { Mode = FilterMode.Advanced, ComparisonText = "Id == 100" };

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToAdvanced_DegradedTextOnly_IsFalse()
    {
        // Same degraded shape but going Advanced — ApplyModeSwitch preserves the existing ComparisonText, so
        // there is no loss and no confirm prompt should fire.
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            ComparisonText = FilterTestConstants.FilterComputerNameEqualsServer01
        };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Advanced));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToAdvanced_FormattableStructure_IsFalse()
    {
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            }
        };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Advanced));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToAdvanced_IncompleteSubFilter_IsTrue()
    {
        // Predicate has property but no value — strict formatter refuses, so going Advanced would silently
        // drop the incomplete predicate without the user noticing.
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            }
        };

        draft.Predicates.Add(new FilterPredicateDraft
        {
            Comparison = new FilterComparisonDraft
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = string.Empty
            },
            JoinWithAny = false
        });

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Advanced));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToCached_DegradedTextOnly_IsTrue()
    {
        // Degraded persisted-Basic shape (LoadFromPersisted couldn't recover BasicFilter from non-decomposable
        // text). Switching to Cached would discard the only data the row carries — the raw text — so the
        // confirm prompt must fire even though structure is empty.
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            ComparisonText = FilterTestConstants.FilterComputerNameEqualsServer01
        };

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToCached_EmptyStructure_IsFalse()
    {
        FilterDraft draft = new() { Mode = FilterMode.Basic };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToCached_HasMeaningfulStructure_IsTrue()
    {
        // Basic mode's ComparisonText is empty/stale by design (only refreshed on save). The mode-switch
        // confirm must inspect HasMeaningfulStructure — checking text alone would let structured input
        // silently disappear when switching to Cached.
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Value = "100"
            }
        };

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_CachedToAdvanced_IsFalse()
    {
        // Cached → Advanced is loss-free: the text is preserved verbatim as the Advanced expression.
        FilterDraft draft = new()
            { Mode = FilterMode.Cached, ComparisonText = FilterTestConstants.FilterIdEquals100 };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Advanced));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_SameMode_IsFalse()
    {
        FilterDraft draft = new()
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Value = "100"
            }
        };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Basic));
    }
}

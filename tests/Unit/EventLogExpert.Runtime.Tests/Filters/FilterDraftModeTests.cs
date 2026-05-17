// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;

namespace EventLogExpert.Runtime.Tests.Filters;

public sealed class FilterDraftModeTests
{
    [Fact]
    public void ApplyModeSwitch_AdvancedToBasic_DecomposableText_HydratesStructure()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = Constants.FilterIdEquals100 };

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
        var draft = new FilterDraft
        {
            Mode = FilterMode.Advanced,
            ComparisonText = Constants.FilterComputerNameEqualsServer01
        };

        draft.ApplyModeSwitch(FilterMode.Basic);

        Assert.Equal(FilterMode.Basic, draft.Mode);
        Assert.Equal(string.Empty, draft.ComparisonText);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.SubFilters);
    }

    [Fact]
    public void ApplyModeSwitch_BasicToAdvanced_DegradedTextOnly_PreservesText()
    {
        // Degraded persisted-Basic shape: ComparisonText carries the only signal the row has, structure is
        // empty because the BasicFilter blob couldn't be recovered. Going Advanced must surface the raw text
        // so the user can repair it manually — blanking it would destroy the only data the row carried.
        var draft = new FilterDraft
        {
            Mode = FilterMode.Basic,
            ComparisonText = Constants.FilterComputerNameEqualsServer01
        };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal(Constants.FilterComputerNameEqualsServer01, draft.ComparisonText);
    }

    [Fact]
    public void ApplyModeSwitch_BasicToAdvanced_EmptyStructureAndEmptyText_StaysEmpty()
    {
        // Brand-new empty Basic row going Advanced: nothing to preserve, nothing to wipe.
        var draft = new FilterDraft { Mode = FilterMode.Basic };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal(string.Empty, draft.ComparisonText);
    }

    [Fact]
    public void ApplyModeSwitch_BasicToAdvanced_FormattableStructure_PopulatesText()
    {
        var draft = new FilterDraft { Mode = FilterMode.Basic };
        draft.Comparison.Property = EventProperty.Id;
        draft.Comparison.Operator = ComparisonOperator.Equals;
        draft.Comparison.MatchMode = MatchMode.Single;
        draft.Comparison.Value = "100";

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal("Id == \"100\"", draft.ComparisonText);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.SubFilters);
    }

    [Fact]
    public void ApplyModeSwitch_BasicToAdvanced_IncompleteSubFilter_FallsBackToLenient()
    {
        // After user-confirmed loss, lenient formatter drops the incomplete sub-filter so the user gets
        // text representing the parts that were valid; an empty result clears the text outright.
        var draft = new FilterDraft { Mode = FilterMode.Basic };
        draft.Comparison.Property = EventProperty.Id;
        draft.Comparison.Operator = ComparisonOperator.Equals;
        draft.Comparison.MatchMode = MatchMode.Single;
        draft.Comparison.Value = "100";

        draft.SubFilters.Add(new SubFilterDraft
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
        Assert.Empty(draft.SubFilters);
    }

    [Fact]
    public void ApplyModeSwitch_CachedToAdvanced_PreservesText()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = Constants.FilterIdEquals100 };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal(Constants.FilterIdEquals100, draft.ComparisonText);
    }

    [Fact]
    public void ApplyModeSwitch_CachedToBasic_DecomposableText_HydratesStructure()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = Constants.FilterIdEquals100 };

        draft.ApplyModeSwitch(FilterMode.Basic);

        Assert.Equal(FilterMode.Basic, draft.Mode);
        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Equal("100", draft.Comparison.Value);
    }

    [Fact]
    public void ApplyModeSwitch_SameMode_IsNoOp()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 100" };

        draft.ApplyModeSwitch(FilterMode.Advanced);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal("Id == 100", draft.ComparisonText);
    }

    [Fact]
    public void ApplyModeSwitch_ToCached_ClearsTextAndStructure()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 100" };
        draft.Comparison.Value = "200";
        draft.SubFilters.Add(new SubFilterDraft());

        draft.ApplyModeSwitch(FilterMode.Cached);

        Assert.Equal(FilterMode.Cached, draft.Mode);
        Assert.Equal(string.Empty, draft.ComparisonText);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.SubFilters);
    }

    [Fact]
    public void HasMeaningfulStructure_ComparisonValueSet_IsTrue()
    {
        var draft = new FilterDraft();
        draft.Comparison.Value = "100";

        Assert.True(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_ComparisonValuesPopulated_IsTrue()
    {
        var draft = new FilterDraft();
        draft.Comparison.Values.Add("Error");

        Assert.True(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_DefaultDraft_IsFalse()
    {
        var draft = new FilterDraft();

        Assert.False(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_OnlyPropertyDefaulted_IsFalse()
    {
        // FilterComparisonDraft.Property defaults to the first enum value (the dropdown's initial selection).
        // The default selection is not "user input" — without a value, save validation must reject as empty.
        var draft = new FilterDraft();
        draft.Comparison.Property = EventProperty.Source;

        Assert.False(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void HasMeaningfulStructure_SubFilterPresent_IsTrue()
    {
        var draft = new FilterDraft();
        draft.SubFilters.Add(new SubFilterDraft());

        Assert.True(draft.HasMeaningfulStructure);
    }

    [Fact]
    public void TryBuildSavedFilter_AdvancedMode_CompileFailure_ReturnsCompileError()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id ===== ###" };

        bool ok = draft.TryBuildSavedFilter(out var saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_AdvancedMode_DecomposableText_ProducesAdvancedSavedFilterWithoutBasicFilter()
    {
        // L4b intent guard: even when the text decomposes cleanly, an Advanced-mode draft saves as Advanced.
        // The row reopens on the Advanced surface on next edit, no silent surface flip.
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = Constants.FilterIdEquals100 };

        bool ok = draft.TryBuildSavedFilter(out var saved, out string error);

        Assert.True(ok, error);
        Assert.NotNull(saved);
        Assert.Equal(FilterMode.Advanced, saved.Mode);
        Assert.Null(saved.BasicFilter);
    }

    [Fact]
    public void TryBuildSavedFilter_AdvancedMode_EmptyText_ReturnsError()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = string.Empty };

        bool ok = draft.TryBuildSavedFilter(out var saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_BasicMode_EmptyStructure_ReturnsErrorMessage()
    {
        var draft = new FilterDraft { Mode = FilterMode.Basic };

        bool ok = draft.TryBuildSavedFilter(out var saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_BasicMode_IncompleteSubFilter_ReturnsError()
    {
        // The strict-subfilters formatter overload exists specifically for this gate: an incomplete sub-filter
        // (e.g. property selected but value blank) must NOT silently disappear from the saved expression.
        var draft = new FilterDraft
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

        draft.SubFilters.Add(new SubFilterDraft
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

        bool ok = draft.TryBuildSavedFilter(out var saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_BasicMode_StructuredInput_BuildsBasicSavedFilter()
    {
        var draft = new FilterDraft
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

        bool ok = draft.TryBuildSavedFilter(out var saved, out string error);

        Assert.True(ok, error);
        Assert.NotNull(saved);
        Assert.Equal(FilterMode.Basic, saved.Mode);
        Assert.NotNull(saved.BasicFilter);
        Assert.Equal("Id == \"100\"", saved.ComparisonText);
    }

    [Fact]
    public void TryBuildSavedFilter_CachedMode_EmptyText_ReturnsError()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = string.Empty };

        bool ok = draft.TryBuildSavedFilter(out var saved, out string error);

        Assert.False(ok);
        Assert.Null(saved);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryBuildSavedFilter_CachedMode_WithText_ProducesCachedSavedFilter()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = Constants.FilterIdEquals100 };

        bool ok = draft.TryBuildSavedFilter(out var saved, out string error);

        Assert.True(ok, error);
        Assert.NotNull(saved);
        Assert.Equal(FilterMode.Cached, saved.Mode);
        Assert.Null(saved.BasicFilter);
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_AdvancedToBasic_DecomposableText_IsFalse()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = Constants.FilterIdEquals100 };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Basic));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_AdvancedToBasic_NonDecomposableText_IsTrue()
    {
        var draft = new FilterDraft
        {
            Mode = FilterMode.Advanced,
            ComparisonText = Constants.FilterComputerNameEqualsServer01
        };

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Basic));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_AdvancedToCached_EmptyText_IsFalse()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = string.Empty };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_AdvancedToCached_NonEmptyText_IsTrue()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 100" };

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToAdvanced_DegradedTextOnly_IsFalse()
    {
        // Same degraded shape but going Advanced — ApplyModeSwitch preserves the existing ComparisonText, so
        // there is no loss and no confirm prompt should fire.
        var draft = new FilterDraft
        {
            Mode = FilterMode.Basic,
            ComparisonText = Constants.FilterComputerNameEqualsServer01
        };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Advanced));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToAdvanced_FormattableStructure_IsFalse()
    {
        var draft = new FilterDraft { Mode = FilterMode.Basic };
        draft.Comparison.Property = EventProperty.Id;
        draft.Comparison.Operator = ComparisonOperator.Equals;
        draft.Comparison.MatchMode = MatchMode.Single;
        draft.Comparison.Value = "100";

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Advanced));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToAdvanced_IncompleteSubFilter_IsTrue()
    {
        // Sub-filter has property but no value — strict formatter refuses, so going Advanced would silently
        // drop the incomplete sub-filter without the user noticing.
        var draft = new FilterDraft { Mode = FilterMode.Basic };
        draft.Comparison.Property = EventProperty.Id;
        draft.Comparison.Operator = ComparisonOperator.Equals;
        draft.Comparison.MatchMode = MatchMode.Single;
        draft.Comparison.Value = "100";

        draft.SubFilters.Add(new SubFilterDraft
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
        var draft = new FilterDraft
        {
            Mode = FilterMode.Basic,
            ComparisonText = Constants.FilterComputerNameEqualsServer01
        };

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToCached_EmptyStructure_IsFalse()
    {
        var draft = new FilterDraft { Mode = FilterMode.Basic };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_BasicToCached_HasMeaningfulStructure_IsTrue()
    {
        // Basic mode's ComparisonText is empty/stale by design (only refreshed on save). The mode-switch
        // confirm must inspect HasMeaningfulStructure — checking text alone would let structured input
        // silently disappear when switching to Cached.
        var draft = new FilterDraft { Mode = FilterMode.Basic };
        draft.Comparison.Value = "100";

        Assert.True(draft.WouldLoseDataSwitchingTo(FilterMode.Cached));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_CachedToAdvanced_IsFalse()
    {
        // Cached → Advanced is loss-free: the text is preserved verbatim as the Advanced expression.
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = Constants.FilterIdEquals100 };

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Advanced));
    }

    [Fact]
    public void WouldLoseDataSwitchingTo_SameMode_IsFalse()
    {
        var draft = new FilterDraft { Mode = FilterMode.Basic };
        draft.Comparison.Value = "100";

        Assert.False(draft.WouldLoseDataSwitchingTo(FilterMode.Basic));
    }
}

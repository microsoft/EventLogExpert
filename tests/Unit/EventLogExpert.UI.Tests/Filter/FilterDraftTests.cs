// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class FilterDraftModelTests
{
    [Fact]
    public void FromSavedFilter_DeepCopiesValuesList_SoEditorMutationDoesNotAffectModel()
    {
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            },
            []);

        var original = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            basicFilter: basicFilter);

        var draft = FilterDraft.FromSavedFilter(original);

        draft.Comparison.Values.Clear();
        draft.Comparison.Values.Add(Constants.FilterValue500);

        Assert.NotNull(original.BasicFilter);
        Assert.Equal(2, original.BasicFilter.Comparison.Values.Count);
        Assert.Equal(Constants.FilterValue100, original.BasicFilter.Comparison.Values[0]);
    }

    [Fact]
    public void FromSavedFilter_HydratesComparisonAndSubFiltersFromBasicFilter()
    {
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = Constants.FilterValue100,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            },
            [
                new SubFilter(
                    new BasicFilterCondition
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    true)
            ]);

        var original = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals100,
            basicFilter: basicFilter);

        var draft = FilterDraft.FromSavedFilter(original);

        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Equal(Constants.FilterValue100, draft.Comparison.Value);
        Assert.Equal(2, draft.Comparison.Values.Count);

        Assert.Single(draft.SubFilters);
        Assert.True(draft.SubFilters[0].JoinWithAny);
        Assert.Equal(EventProperty.Level, draft.SubFilters[0].Condition.Property);
        Assert.Equal("Error", draft.SubFilters[0].Condition.Value);
    }

    [Fact]
    public void FromSavedFilter_PreservesId()
    {
        var original = FilterUtils.CreateTestFilter();

        var draft = FilterDraft.FromSavedFilter(original);

        Assert.Equal(original.Id, draft.Id);
    }

    [Fact]
    public void FromSavedFilter_PreservesScalarFields()
    {
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = Constants.FilterValue100
            },
            []);

        var original = FilterUtils.CreateTestFilter(
            color: HighlightColor.Blue,
            basicFilter: basicFilter,
            isEnabled: true,
            isExcluded: true);

        var draft = FilterDraft.FromSavedFilter(original);

        Assert.Equal(HighlightColor.Blue, draft.Color);
        Assert.Equal(FilterMode.Basic, draft.Mode);
        Assert.True(draft.IsEnabled);
        Assert.True(draft.IsExcluded);
        Assert.Equal(Constants.FilterIdEquals100, draft.ComparisonText);
    }

    [Fact]
    public void FromSavedFilter_WhenNoBasicFilter_LeavesComparisonAndSubFiltersEmpty()
    {
        // Advanced/Cached saved filters reopen with empty structure regardless of whether the persisted
        // text happens to be Basic-vocabulary; FromSavedFilter gates structure hydration on Mode == Basic
        // so a future opportunistic-decompose path can't silently flip the row's mode on re-edit.
        var original = new SavedFilter
        {
            ComparisonText = Constants.FilterIdEquals100,
            Compiled = FilterCompiler.TryCompile(Constants.FilterIdEquals100, out var compiled, out _) ? compiled : null,
            BasicFilter = null,
            Mode = FilterMode.Advanced,
            IsEnabled = true
        };

        var draft = FilterDraft.FromSavedFilter(original);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.Comparison.Values);
        Assert.Empty(draft.SubFilters);
        Assert.Equal("Id == 100", draft.ComparisonText);
    }

    [Fact]
    public void FromSavedFilter_AdvancedModeWithStaleBasicFilter_DoesNotHydrateStructure()
    {
        // Defensive: even if a SavedFilter somehow carries Mode=Advanced + a non-null BasicFilter (e.g. a
        // hand-edited persistence file), FromSavedFilter must NOT hydrate structure — Mode wins.
        var staleBasic = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = Constants.FilterValue100
            },
            []);

        var original = new SavedFilter
        {
            ComparisonText = Constants.FilterIdEquals100,
            Compiled = FilterCompiler.TryCompile(Constants.FilterIdEquals100, out var compiled, out _) ? compiled : null,
            BasicFilter = staleBasic,
            Mode = FilterMode.Advanced,
            IsEnabled = true
        };

        var draft = FilterDraft.FromSavedFilter(original);

        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.SubFilters);
    }

    [Fact]
    public void ToBasicFilter_DoesNotShareValuesListWithDraft()
    {
        var draft = FilterUtils.CreateTestFilterDraft(
            comparison: new FilterConditionDraft
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = ["Error"]
            });

        var source = draft.ToBasicFilter();

        draft.Comparison.Values.Add("Warning");

        Assert.Single(source.Comparison.Values);
        Assert.Equal("Error", source.Comparison.Values[0]);
    }

    [Fact]
    public void ToBasicFilter_ProducesImmutableSourceMatchingEditorState()
    {
        var draft = FilterUtils.CreateTestFilterDraft(
            comparison: new FilterConditionDraft
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = Constants.FilterValue100
            },
            subFilters:
            [
                new SubFilterDraft
                {
                    Condition = new FilterConditionDraft
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    JoinWithAny = true
                }
            ]);

        var source = draft.ToBasicFilter();

        Assert.Equal(EventProperty.Id, source.Comparison.Property);
        Assert.Equal(Constants.FilterValue100, source.Comparison.Value);
        Assert.Single(source.SubFilters);
        Assert.True(source.SubFilters[0].JoinWithAny);
        Assert.Equal(EventProperty.Level, source.SubFilters[0].Data.Property);
        Assert.Equal("Error", source.SubFilters[0].Data.Value);
    }
}

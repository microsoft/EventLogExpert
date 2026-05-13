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
            comparisonValue: Constants.FilterIdEquals100,
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
                    JoinWithAny: true)
            ]);

        var original = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100,
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
        Assert.True(draft.IsBasic);
        Assert.True(draft.IsEnabled);
        Assert.True(draft.IsExcluded);
        Assert.Equal(Constants.FilterIdEquals100, draft.ComparisonText);
    }

    [Fact]
    public void FromSavedFilter_WhenNoBasicFilter_LeavesComparisonAndSubFiltersEmpty()
    {
        // Advanced filters expose raw ComparisonText without populating structured draft inputs.
        var original = FilterUtils.CreateTestFilter(comparisonValue: Constants.FilterIdEquals100);

        var draft = FilterDraft.FromSavedFilter(original);

        Assert.False(draft.IsBasic);
        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.Comparison.Values);
        Assert.Empty(draft.SubFilters);
        Assert.Equal("Id == 100", draft.ComparisonText);
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

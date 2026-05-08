// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterDraftModelTests
{
    [Fact]
    public void FromFilterModel_DeepCopiesValuesList_SoEditorMutationDoesNotAffectModel()
    {
        var basicFilter = new BasicFilter(
            new FilterData
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.MultiSelect,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            },
            []);

        var original = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100,
            filterType: FilterType.Basic,
            basicFilter: basicFilter);

        var draft = FilterDraftModel.FromFilterModel(original);

        draft.Comparison.Values.Clear();
        draft.Comparison.Values.Add(Constants.FilterValue500);

        Assert.NotNull(original.BasicFilter);
        Assert.Equal(2, original.BasicFilter.Comparison.Values.Count);
        Assert.Equal(Constants.FilterValue100, original.BasicFilter.Comparison.Values[0]);
    }

    [Fact]
    public void FromFilterModel_HydratesComparisonAndSubFiltersFromBasicFilter()
    {
        var basicFilter = new BasicFilter(
            new FilterData
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.Equals,
                Value = Constants.FilterValue100,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            },
            [
                new SubFilter(
                    new FilterData
                    {
                        Category = FilterCategory.Level,
                        Evaluator = FilterEvaluator.Equals,
                        Value = "Error"
                    },
                    JoinWithAny: true)
            ]);

        var original = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100,
            filterType: FilterType.Basic,
            basicFilter: basicFilter);

        var draft = FilterDraftModel.FromFilterModel(original);

        Assert.Equal(FilterCategory.Id, draft.Comparison.Category);
        Assert.Equal(Constants.FilterValue100, draft.Comparison.Value);
        Assert.Equal(2, draft.Comparison.Values.Count);

        Assert.Single(draft.SubFilters);
        Assert.True(draft.SubFilters[0].JoinWithAny);
        Assert.Equal(FilterCategory.Level, draft.SubFilters[0].Data.Category);
        Assert.Equal("Error", draft.SubFilters[0].Data.Value);
    }

    [Fact]
    public void FromFilterModel_PreservesId()
    {
        var original = FilterUtils.CreateTestFilter();

        var draft = FilterDraftModel.FromFilterModel(original);

        Assert.Equal(original.Id, draft.Id);
    }

    [Fact]
    public void FromFilterModel_PreservesScalarFields()
    {
        var original = FilterUtils.CreateTestFilter(
            color: HighlightColor.Blue,
            filterType: FilterType.Basic,
            isEnabled: true,
            isExcluded: true);

        var draft = FilterDraftModel.FromFilterModel(original);

        Assert.Equal(HighlightColor.Blue, draft.Color);
        Assert.Equal(FilterType.Basic, draft.FilterType);
        Assert.True(draft.IsEnabled);
        Assert.True(draft.IsExcluded);
        Assert.Equal(Constants.FilterIdEquals100, draft.ComparisonText);
    }

    [Fact]
    public void FromFilterModel_WhenNoBasicFilter_LeavesComparisonAndSubFiltersEmpty()
    {
        // Advanced filters and legacy basic filters lacking BasicFilter simply expose the
        // raw ComparisonText for re-edit without populating the structured draft inputs.
        var original = FilterUtils.CreateTestFilter(comparisonValue: Constants.FilterIdEquals100);

        var draft = FilterDraftModel.FromFilterModel(original);

        Assert.Equal(FilterCategory.Id, draft.Comparison.Category);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.Comparison.Values);
        Assert.Empty(draft.SubFilters);
        Assert.Equal("Id == 100", draft.ComparisonText);
    }

    [Fact]
    public void ToBasicFilter_DoesNotShareValuesListWithDraft()
    {
        var draft = FilterUtils.CreateTestFilterDraft(
            comparison: new FilterDataDraft
            {
                Category = FilterCategory.Level,
                Evaluator = FilterEvaluator.MultiSelect,
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
            comparison: new FilterDataDraft
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.Equals,
                Value = Constants.FilterValue100
            },
            subFilters:
            [
                new SubFilterDraft
                {
                    Data = new FilterDataDraft
                    {
                        Category = FilterCategory.Level,
                        Evaluator = FilterEvaluator.Equals,
                        Value = "Error"
                    },
                    JoinWithAny = true
                }
            ]);

        var source = draft.ToBasicFilter();

        Assert.Equal(FilterCategory.Id, source.Comparison.Category);
        Assert.Equal(Constants.FilterValue100, source.Comparison.Value);
        Assert.Single(source.SubFilters);
        Assert.True(source.SubFilters[0].JoinWithAny);
        Assert.Equal(FilterCategory.Level, source.SubFilters[0].Data.Category);
        Assert.Equal("Error", source.SubFilters[0].Data.Value);
    }
}

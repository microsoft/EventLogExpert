// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterEditorModelTests
{
    [Fact]
    public void FromFilterModel_DeepCopiesData()
    {
        var original = FilterUtils.CreateTestFilter(
            data: new FilterData
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.Equals,
                Value = Constants.FilterValue100,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            });

        var editor = FilterEditorModel.FromFilterModel(original);

        // Mutating the editor copy must not affect the original.
        editor.Data.Value = Constants.FilterValue500;
        editor.Data.Values.Clear();

        Assert.Equal(Constants.FilterValue100, original.Data.Value);
        Assert.Equal(2, original.Data.Values.Count);
    }

    [Fact]
    public void FromFilterModel_PreservesId()
    {
        var original = FilterUtils.CreateTestFilter();

        var editor = FilterEditorModel.FromFilterModel(original);

        Assert.Equal(original.Id, editor.Id);
    }

    [Fact]
    public void FromFilterModel_PreservesScalarFields()
    {
        var original = FilterUtils.CreateTestFilter(
            color: HighlightColor.Blue,
            filterType: FilterType.Basic,
            shouldCompareAny: true,
            isEnabled: true,
            isExcluded: true);

        var editor = FilterEditorModel.FromFilterModel(original);

        Assert.Equal(HighlightColor.Blue, editor.Color);
        Assert.Equal(FilterType.Basic, editor.FilterType);
        Assert.True(editor.ShouldCompareAny);
        Assert.True(editor.IsEnabled);
        Assert.True(editor.IsExcluded);
        Assert.Equal(Constants.FilterIdEquals100, editor.ComparisonText);
    }

    [Fact]
    public void FromFilterModel_RecursivelyConvertsSubFilters()
    {
        var grandchild = FilterUtils.CreateTestFilter(comparisonValue: Constants.FilterIdGreaterThan100);
        var child1 = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100,
            isExcluded: true,
            subFilters: [grandchild]);
        var child2 = FilterUtils.CreateTestFilter(comparisonValue: Constants.FilterIdEquals200);
        var parent = FilterUtils.CreateTestFilter(subFilters: [child1, child2]);

        var editor = FilterEditorModel.FromFilterModel(parent);

        Assert.Equal(2, editor.SubFilters.Count);
        Assert.Equal(child1.Id, editor.SubFilters[0].Id);
        Assert.Equal(child2.Id, editor.SubFilters[1].Id);
        Assert.True(editor.SubFilters[0].IsExcluded);
        Assert.Single(editor.SubFilters[0].SubFilters);
        Assert.Equal(grandchild.Id, editor.SubFilters[0].SubFilters[0].Id);
    }

    [Fact]
    public void RoundTrip_PreservesDeepTreeIdentityAndOrder()
    {
        var grandchildA = FilterUtils.CreateTestFilter(comparisonValue: Constants.FilterIdEquals100);
        var grandchildB = FilterUtils.CreateTestFilter(comparisonValue: Constants.FilterIdEquals200);
        var child = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdGreaterThan100,
            isExcluded: true,
            subFilters: [grandchildA, grandchildB]);
        var original = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100AndLevelError,
            color: HighlightColor.Green,
            filterType: FilterType.Basic,
            shouldCompareAny: true,
            isEnabled: true,
            data: new FilterData
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.Equals,
                Value = Constants.FilterValue100
            },
            subFilters: [child]);

        var roundTripped = FilterEditorModel.FromFilterModel(original).ToFilterModel();

        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Color, roundTripped.Color);
        Assert.Equal(original.FilterType, roundTripped.FilterType);
        Assert.Equal(original.ShouldCompareAny, roundTripped.ShouldCompareAny);
        Assert.Equal(original.IsEnabled, roundTripped.IsEnabled);
        Assert.Equal(original.IsExcluded, roundTripped.IsExcluded);
        Assert.Equal(original.Comparison.Value, roundTripped.Comparison.Value);
        Assert.Equal(original.Data.Category, roundTripped.Data.Category);
        Assert.Equal(original.Data.Evaluator, roundTripped.Data.Evaluator);
        Assert.Equal(original.Data.Value, roundTripped.Data.Value);

        Assert.Single(roundTripped.SubFilters);
        Assert.Equal(child.Id, roundTripped.SubFilters[0].Id);
        Assert.True(roundTripped.SubFilters[0].IsExcluded);
        Assert.Equal(2, roundTripped.SubFilters[0].SubFilters.Count);
        Assert.Equal(grandchildA.Id, roundTripped.SubFilters[0].SubFilters[0].Id);
        Assert.Equal(grandchildB.Id, roundTripped.SubFilters[0].SubFilters[1].Id);
    }

    [Fact]
    public void ToFilterModel_DeepCopiesData_SoSubsequentEditorMutationDoesNotAffectModel()
    {
        var editor = FilterUtils.CreateTestFilterEditor(
            data: new FilterData
            {
                Category = FilterCategory.Id,
                Value = Constants.FilterValue100,
                Values = [Constants.FilterValue100]
            });

        var model = editor.ToFilterModel();

        editor.Data.Value = Constants.FilterValue500;
        editor.Data.Values.Add(Constants.FilterValue1000);

        Assert.Equal(Constants.FilterValue100, model.Data.Value);
        Assert.Single(model.Data.Values);
    }

    [Fact]
    public void ToFilterModel_PreservesIdAndAllFields()
    {
        var id = FilterId.Create();
        var editor = FilterUtils.CreateTestFilterEditor(
            id: id,
            comparisonText: Constants.FilterIdEquals200,
            color: HighlightColor.Red,
            filterType: FilterType.Basic,
            shouldCompareAny: true,
            isEnabled: true,
            isExcluded: true,
            data: new FilterData
            {
                Category = FilterCategory.Source,
                Evaluator = FilterEvaluator.Contains,
                Value = Constants.EventSourceTestSource
            });

        var model = editor.ToFilterModel();

        Assert.Equal(id, model.Id);
        Assert.Equal(HighlightColor.Red, model.Color);
        Assert.Equal(Constants.FilterIdEquals200, model.Comparison.Value);
        Assert.Equal(FilterType.Basic, model.FilterType);
        Assert.True(model.ShouldCompareAny);
        Assert.True(model.IsEnabled);
        Assert.True(model.IsExcluded);
        Assert.Equal(FilterCategory.Source, model.Data.Category);
        Assert.Equal(FilterEvaluator.Contains, model.Data.Evaluator);
        Assert.Equal(Constants.EventSourceTestSource, model.Data.Value);
    }

    [Fact]
    public void ToFilterModel_RecursivelyConvertsSubFilters()
    {
        var childId = FilterId.Create();
        var grandchildId = FilterId.Create();
        var editor = FilterUtils.CreateTestFilterEditor(
            comparisonText: string.Empty,
            subFilters:
            [
                FilterUtils.CreateTestFilterEditor(
                    id: childId,
                    comparisonText: Constants.FilterIdEquals100,
                    subFilters:
                    [
                        FilterUtils.CreateTestFilterEditor(id: grandchildId, comparisonText: Constants.FilterIdEquals200)
                    ])
            ]);

        var model = editor.ToFilterModel();

        Assert.Single(model.SubFilters);
        Assert.Equal(childId, model.SubFilters[0].Id);
        Assert.Single(model.SubFilters[0].SubFilters);
        Assert.Equal(grandchildId, model.SubFilters[0].SubFilters[0].Id);
    }

    [Fact]
    public void ToFilterModel_WithEmptyComparisonText_DoesNotCompileExpression()
    {
        var editor = FilterUtils.CreateTestFilterEditor(comparisonText: string.Empty);

        // Must not throw — default FilterComparison has no compiled Expression and ComparisonText is empty.
        var model = editor.ToFilterModel();

        Assert.Equal(string.Empty, model.Comparison.Value);
    }
}

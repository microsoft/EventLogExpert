// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterEditorModelTests
{
    [Fact]
    public void FromFilterModel_DeepCopiesMainCriteria()
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
        editor.Main.Value = Constants.FilterValue500;
        editor.Main.Values.Clear();

        Assert.Equal(Constants.FilterValue100, original.Data.Value);
        Assert.Equal(2, original.Data.Values.Count);
    }

    [Fact]
    public void FromFilterModel_DiscardsLegacyGrandchildSubFilters()
    {
        // 13c intentionally flattens deeper trees: production never produces them.
        var grandchild = FilterUtils.CreateTestFilter();
        var child = FilterUtils.CreateTestFilter(subFilters: [grandchild]);
        var parent = FilterUtils.CreateTestFilter(subFilters: [child]);

        var editor = FilterEditorModel.FromFilterModel(parent);

        Assert.Single(editor.SubClauses);
        // Grandchildren are not represented anywhere on the editor model.
    }

    [Fact]
    public void FromFilterModel_FlattensTopLevelSubFiltersIntoSubClauses()
    {
        var subA = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100,
            shouldCompareAny: true,
            data: new FilterData { Category = FilterCategory.Level, Evaluator = FilterEvaluator.Equals, Value = "Error" });

        var subB = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals200,
            shouldCompareAny: false,
            data: new FilterData { Category = FilterCategory.Source, Evaluator = FilterEvaluator.Contains, Value = "Kernel" });

        var parent = FilterUtils.CreateTestFilter(subFilters: [subA, subB]);

        var editor = FilterEditorModel.FromFilterModel(parent);

        Assert.Equal(2, editor.SubClauses.Count);
        Assert.Equal(subA.Id, editor.SubClauses[0].Id);
        Assert.Equal(subB.Id, editor.SubClauses[1].Id);
        Assert.True(editor.SubClauses[0].JoinWithAny);
        Assert.False(editor.SubClauses[1].JoinWithAny);
        Assert.Equal(FilterCategory.Level, editor.SubClauses[0].Criteria.Category);
        Assert.Equal("Error", editor.SubClauses[0].Criteria.Value);
        Assert.Equal(FilterCategory.Source, editor.SubClauses[1].Criteria.Category);
        Assert.Equal("Kernel", editor.SubClauses[1].Criteria.Value);
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
            isEnabled: true,
            isExcluded: true);

        var editor = FilterEditorModel.FromFilterModel(original);

        Assert.Equal(HighlightColor.Blue, editor.Color);
        Assert.Equal(FilterType.Basic, editor.FilterType);
        Assert.True(editor.IsEnabled);
        Assert.True(editor.IsExcluded);
        Assert.Equal(Constants.FilterIdEquals100, editor.ComparisonText);
    }

    [Fact]
    public void RoundTrip_PreservesScalarsAndFlatSubClauses()
    {
        var subA = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100,
            shouldCompareAny: true,
            data: new FilterData { Category = FilterCategory.Level, Evaluator = FilterEvaluator.Equals, Value = "Error" });
        var subB = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals200,
            shouldCompareAny: false,
            data: new FilterData { Category = FilterCategory.Source, Evaluator = FilterEvaluator.Contains, Value = "Kernel" });
        var original = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100AndLevelError,
            color: HighlightColor.Green,
            filterType: FilterType.Basic,
            isEnabled: true,
            data: new FilterData
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.Equals,
                Value = Constants.FilterValue100
            },
            subFilters: [subA, subB]);

        var roundTripped = FilterEditorModel.FromFilterModel(original).ToFilterModel();

        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Color, roundTripped.Color);
        Assert.Equal(original.FilterType, roundTripped.FilterType);
        Assert.Equal(original.IsEnabled, roundTripped.IsEnabled);
        Assert.Equal(original.IsExcluded, roundTripped.IsExcluded);
        Assert.Equal(original.Comparison.Value, roundTripped.Comparison.Value);
        Assert.Equal(original.Data.Category, roundTripped.Data.Category);
        Assert.Equal(original.Data.Evaluator, roundTripped.Data.Evaluator);
        Assert.Equal(original.Data.Value, roundTripped.Data.Value);

        Assert.Equal(2, roundTripped.SubFilters.Count);
        Assert.Equal(subA.Id, roundTripped.SubFilters[0].Id);
        Assert.Equal(subB.Id, roundTripped.SubFilters[1].Id);
        Assert.True(roundTripped.SubFilters[0].ShouldCompareAny);
        Assert.False(roundTripped.SubFilters[1].ShouldCompareAny);
        Assert.Equal(FilterCategory.Level, roundTripped.SubFilters[0].Data.Category);
        Assert.Equal("Error", roundTripped.SubFilters[0].Data.Value);
    }

    [Fact]
    public void ToBasicSource_DoesNotShareValuesListWithEditor()
    {
        var editor = FilterUtils.CreateTestFilterEditor(
            main: new BasicFilterCriteriaDraft
            {
                Category = FilterCategory.Level,
                Evaluator = FilterEvaluator.MultiSelect,
                Values = ["Error"]
            });

        var source = editor.ToBasicSource();

        editor.Main.Values.Add("Warning");

        Assert.Single(source.Main.Values);
        Assert.Equal("Error", source.Main.Values[0]);
    }

    [Fact]
    public void ToBasicSource_ProducesImmutableSourceMatchingEditorState()
    {
        var editor = FilterUtils.CreateTestFilterEditor(
            main: new BasicFilterCriteriaDraft
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.Equals,
                Value = Constants.FilterValue100
            },
            subClauses:
            [
                new BasicSubClauseDraft
                {
                    Criteria = new BasicFilterCriteriaDraft
                    {
                        Category = FilterCategory.Level,
                        Evaluator = FilterEvaluator.Equals,
                        Value = "Error"
                    },
                    JoinWithAny = true
                }
            ]);

        var source = editor.ToBasicSource();

        Assert.Equal(FilterCategory.Id, source.Main.Category);
        Assert.Equal(Constants.FilterValue100, source.Main.Value);
        Assert.Single(source.SubClauses);
        Assert.True(source.SubClauses[0].JoinWithAny);
        Assert.Equal(FilterCategory.Level, source.SubClauses[0].Criteria.Category);
        Assert.Equal("Error", source.SubClauses[0].Criteria.Value);
    }

    [Fact]
    public void ToFilterModel_DeepCopiesMain_SoSubsequentEditorMutationDoesNotAffectModel()
    {
        var editor = FilterUtils.CreateTestFilterEditor(
            main: new BasicFilterCriteriaDraft
            {
                Category = FilterCategory.Id,
                Value = Constants.FilterValue100,
                Values = [Constants.FilterValue100]
            });

        var model = editor.ToFilterModel();

        editor.Main.Value = Constants.FilterValue500;
        editor.Main.Values.Add(Constants.FilterValue1000);

        Assert.Equal(Constants.FilterValue100, model.Data.Value);
        Assert.Single(model.Data.Values);
    }

    [Fact]
    public void ToFilterModel_MaterializesSubClausesAsDegenerateSubFilters()
    {
        var subClauseId = FilterId.Create();
        var editor = FilterUtils.CreateTestFilterEditor(
            comparisonText: string.Empty,
            subClauses:
            [
                new BasicSubClauseDraft
                {
                    Id = subClauseId,
                    Criteria = new BasicFilterCriteriaDraft
                    {
                        Category = FilterCategory.Level,
                        Evaluator = FilterEvaluator.Equals,
                        Value = "Error"
                    },
                    JoinWithAny = true
                }
            ]);

        var model = editor.ToFilterModel();

        Assert.Single(model.SubFilters);
        Assert.Equal(subClauseId, model.SubFilters[0].Id);
        Assert.True(model.SubFilters[0].ShouldCompareAny);
        Assert.Equal(FilterCategory.Level, model.SubFilters[0].Data.Category);
        Assert.Equal("Error", model.SubFilters[0].Data.Value);
        Assert.Empty(model.SubFilters[0].SubFilters);
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
            isEnabled: true,
            isExcluded: true,
            main: new BasicFilterCriteriaDraft
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
        Assert.True(model.IsEnabled);
        Assert.True(model.IsExcluded);
        Assert.Equal(FilterCategory.Source, model.Data.Category);
        Assert.Equal(FilterEvaluator.Contains, model.Data.Evaluator);
        Assert.Equal(Constants.EventSourceTestSource, model.Data.Value);
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

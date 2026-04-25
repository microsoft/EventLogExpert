// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterEditorModelTests
{
    [Fact]
    public void FromFilterModel_DeepCopiesValuesList_SoEditorMutationDoesNotAffectModel()
    {
        var basicSource = new BasicFilterSource(
            new BasicFilterCriteria
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.MultiSelect,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            },
            []);

        var original = FilterUtils.CreateTestFilter(
            comparisonValue: Constants.FilterIdEquals100,
            filterType: FilterType.Basic,
            basicSource: basicSource);

        var editor = FilterEditorModel.FromFilterModel(original);

        editor.Main.Values.Clear();
        editor.Main.Values.Add(Constants.FilterValue500);

        Assert.Equal(2, original.BasicSource!.Main.Values.Count);
        Assert.Equal(Constants.FilterValue100, original.BasicSource.Main.Values[0]);
    }

    [Fact]
    public void FromFilterModel_HydratesMainAndSubClausesFromBasicSource()
    {
        var basicSource = new BasicFilterSource(
            new BasicFilterCriteria
            {
                Category = FilterCategory.Id,
                Evaluator = FilterEvaluator.Equals,
                Value = Constants.FilterValue100,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            },
            [
                new BasicSubClause(
                    new BasicFilterCriteria
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
            basicSource: basicSource);

        var editor = FilterEditorModel.FromFilterModel(original);

        Assert.Equal(FilterCategory.Id, editor.Main.Category);
        Assert.Equal(Constants.FilterValue100, editor.Main.Value);
        Assert.Equal(2, editor.Main.Values.Count);

        Assert.Single(editor.SubClauses);
        Assert.True(editor.SubClauses[0].JoinWithAny);
        Assert.Equal(FilterCategory.Level, editor.SubClauses[0].Criteria.Category);
        Assert.Equal("Error", editor.SubClauses[0].Criteria.Value);
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
    public void FromFilterModel_WhenNoBasicSource_LeavesMainAndSubClausesEmpty()
    {
        // Advanced filters and legacy basic filters lacking BasicSource simply expose the
        // raw ComparisonText for re-edit without populating the structured editor inputs.
        var original = FilterUtils.CreateTestFilter(comparisonValue: "Id == 100");

        var editor = FilterEditorModel.FromFilterModel(original);

        Assert.Equal(FilterCategory.Id, editor.Main.Category);
        Assert.Null(editor.Main.Value);
        Assert.Empty(editor.Main.Values);
        Assert.Empty(editor.SubClauses);
        Assert.Equal("Id == 100", editor.ComparisonText);
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
}

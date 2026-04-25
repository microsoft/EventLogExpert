// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterDataDraftTests
{
    [Fact]
    public void ChangeCategory_ClearsValueAndValues()
    {
        var draft = new FilterDataDraft
        {
            Category = FilterCategory.Id,
            Value = "100",
            Values = ["100", "200"]
        };

        draft.ChangeCategory(FilterCategory.Source);

        Assert.Null(draft.Value);
        Assert.Empty(draft.Values);
    }

    [Fact]
    public void ChangeCategory_DoesNotResetEvaluator()
    {
        // Evaluator coercion is the UI's responsibility (FilterCategoryEditor.CategoryBinding),
        // not the draft's — keeping it on the draft would entangle non-overlapping concerns.
        var draft = new FilterDataDraft
        {
            Category = FilterCategory.Id,
            Evaluator = FilterEvaluator.MultiSelect
        };

        draft.ChangeCategory(FilterCategory.Description);

        Assert.Equal(FilterEvaluator.MultiSelect, draft.Evaluator);
    }

    [Fact]
    public void ChangeCategory_SetsNewCategory()
    {
        var draft = new FilterDataDraft { Category = FilterCategory.Id };

        draft.ChangeCategory(FilterCategory.Source);

        Assert.Equal(FilterCategory.Source, draft.Category);
    }

    [Fact]
    public void FromData_DoesNotShareValuesListWithDraft()
    {
        var data = new FilterData
        {
            Category = FilterCategory.Level,
            Values = ["Error"]
        };

        var draft = FilterDataDraft.FromData(data);

        draft.Values.Add("Warning");

        Assert.Single(data.Values);
        Assert.Equal("Error", data.Values[0]);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new FilterData
        {
            Category = FilterCategory.Level,
            Evaluator = FilterEvaluator.MultiSelect,
            Value = "Error",
            Values = ["Error", "Warning"]
        };

        var roundTripped = FilterDataDraft.FromData(original).ToData();

        Assert.Equal(original.Category, roundTripped.Category);
        Assert.Equal(original.Evaluator, roundTripped.Evaluator);
        Assert.Equal(original.Value, roundTripped.Value);
        Assert.Equal(original.Values, roundTripped.Values);
    }

    [Fact]
    public void ToData_DoesNotShareValuesListWithDraft()
    {
        var draft = new FilterDataDraft
        {
            Category = FilterCategory.Level,
            Values = ["Error"]
        };

        var data = draft.ToData();

        draft.Values.Add("Warning");

        Assert.Single(data.Values);
        Assert.Equal("Error", data.Values[0]);
    }
}

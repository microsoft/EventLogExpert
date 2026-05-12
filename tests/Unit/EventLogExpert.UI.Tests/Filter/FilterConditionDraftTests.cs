// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class FilterConditionDraftTests
{
    [Fact]
    public void ChangeCategory_ClearsValueAndValues()
    {
        var draft = new FilterConditionDraft
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
        var draft = new FilterConditionDraft
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
        var draft = new FilterConditionDraft { Category = FilterCategory.Id };

        draft.ChangeCategory(FilterCategory.Source);

        Assert.Equal(FilterCategory.Source, draft.Category);
    }

    [Fact]
    public void FromCondition_DoesNotShareValuesListWithDraft()
    {
        var condition = new FilterCondition
        {
            Category = FilterCategory.Level,
            Values = ["Error"]
        };

        var draft = FilterConditionDraft.FromCondition(condition);

        draft.Values.Add("Warning");

        Assert.Single(condition.Values);
        Assert.Equal("Error", condition.Values[0]);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new FilterCondition
        {
            Category = FilterCategory.Level,
            Evaluator = FilterEvaluator.MultiSelect,
            Value = "Error",
            Values = ["Error", "Warning"]
        };

        var roundTripped = FilterConditionDraft.FromCondition(original).ToCondition();

        Assert.Equal(original.Category, roundTripped.Category);
        Assert.Equal(original.Evaluator, roundTripped.Evaluator);
        Assert.Equal(original.Value, roundTripped.Value);
        Assert.Equal(original.Values, roundTripped.Values);
    }

    [Fact]
    public void ToCondition_DoesNotShareValuesListWithDraft()
    {
        var draft = new FilterConditionDraft
        {
            Category = FilterCategory.Level,
            Values = ["Error"]
        };

        var condition = draft.ToCondition();

        draft.Values.Add("Warning");

        Assert.Single(condition.Values);
        Assert.Equal("Error", condition.Values[0]);
    }
}

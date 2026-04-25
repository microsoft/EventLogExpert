// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Tests.Models;

public sealed class BasicFilterCriteriaDraftTests
{
    [Fact]
    public void ChangeCategory_ClearsValueAndValues()
    {
        var draft = new BasicFilterCriteriaDraft
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
        var draft = new BasicFilterCriteriaDraft
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
        var draft = new BasicFilterCriteriaDraft { Category = FilterCategory.Id };

        draft.ChangeCategory(FilterCategory.Source);

        Assert.Equal(FilterCategory.Source, draft.Category);
    }

    [Fact]
    public void FromCriteria_DoesNotShareValuesListWithDraft()
    {
        var criteria = new BasicFilterCriteria
        {
            Category = FilterCategory.Level,
            Values = ["Error"]
        };

        var draft = BasicFilterCriteriaDraft.FromCriteria(criteria);

        draft.Values.Add("Warning");

        Assert.Single(criteria.Values);
        Assert.Equal("Error", criteria.Values[0]);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new BasicFilterCriteria
        {
            Category = FilterCategory.Level,
            Evaluator = FilterEvaluator.MultiSelect,
            Value = "Error",
            Values = ["Error", "Warning"]
        };

        var roundTripped = BasicFilterCriteriaDraft.FromCriteria(original).ToCriteria();

        Assert.Equal(original.Category, roundTripped.Category);
        Assert.Equal(original.Evaluator, roundTripped.Evaluator);
        Assert.Equal(original.Value, roundTripped.Value);
        Assert.Equal(original.Values, roundTripped.Values);
    }

    [Fact]
    public void ToCriteria_DoesNotShareValuesListWithDraft()
    {
        var draft = new BasicFilterCriteriaDraft
        {
            Category = FilterCategory.Level,
            Values = ["Error"]
        };

        var criteria = draft.ToCriteria();

        draft.Values.Add("Warning");

        Assert.Single(criteria.Values);
        Assert.Equal("Error", criteria.Values[0]);
    }
}

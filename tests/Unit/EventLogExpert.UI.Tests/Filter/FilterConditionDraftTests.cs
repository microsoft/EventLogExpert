// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class FilterConditionDraftTests
{
    [Fact]
    public void ChangeProperty_ClearsValueAndValues()
    {
        var draft = new FilterConditionDraft
        {
            Property = EventProperty.Id,
            Value = "100",
            Values = ["100", "200"]
        };

        draft.ChangeProperty(EventProperty.Source);

        Assert.Null(draft.Value);
        Assert.Empty(draft.Values);
    }

    [Fact]
    public void ChangeProperty_DoesNotResetOperatorOrMatchMode()
    {
        // Operator/match-mode coercion is the UI's responsibility, not the draft's.
        var draft = new FilterConditionDraft
        {
            Property = EventProperty.Id,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many
        };

        draft.ChangeProperty(EventProperty.Description);

        Assert.Equal(ComparisonOperator.Equals, draft.Operator);
        Assert.Equal(MatchMode.Many, draft.MatchMode);
    }

    [Fact]
    public void ChangeProperty_SetsNewProperty()
    {
        var draft = new FilterConditionDraft { Property = EventProperty.Id };

        draft.ChangeProperty(EventProperty.Source);

        Assert.Equal(EventProperty.Source, draft.Property);
    }

    [Fact]
    public void FromCondition_DoesNotShareValuesListWithDraft()
    {
        var condition = new BasicFilterCondition
        {
            Property = EventProperty.Level,
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
        var original = new BasicFilterCondition
        {
            Property = EventProperty.Level,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many,
            Value = "Error",
            Values = ["Error", "Warning"]
        };

        var roundTripped = FilterConditionDraft.FromCondition(original).ToCondition();

        Assert.Equal(original.Property, roundTripped.Property);
        Assert.Equal(original.Operator, roundTripped.Operator);
        Assert.Equal(original.MatchMode, roundTripped.MatchMode);
        Assert.Equal(original.Value, roundTripped.Value);
        Assert.Equal(original.Values, roundTripped.Values);
    }

    [Fact]
    public void ToCondition_DoesNotShareValuesListWithDraft()
    {
        var draft = new FilterConditionDraft
        {
            Property = EventProperty.Level,
            Values = ["Error"]
        };

        var condition = draft.ToCondition();

        draft.Values.Add("Warning");

        Assert.Single(condition.Values);
        Assert.Equal("Error", condition.Values[0]);
    }
}

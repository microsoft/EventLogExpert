// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;

namespace EventLogExpert.Filtering.Tests.Drafts;

public sealed class FilterComparisonDraftTests
{
    [Fact]
    public void ChangeProperty_ClearsValueAndValues()
    {
        var draft = new FilterComparisonDraft
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
        var draft = new FilterComparisonDraft
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
        var draft = new FilterComparisonDraft { Property = EventProperty.Id };

        draft.ChangeProperty(EventProperty.Source);

        Assert.Equal(EventProperty.Source, draft.Property);
    }

    [Fact]
    public void FromComparison_DoesNotShareValuesListWithDraft()
    {
        var comparison = new FilterComparison
        {
            Property = EventProperty.Level,
            Values = ["Error"]
        };

        var draft = FilterComparisonDraft.FromComparison(comparison);

        draft.Values.Add("Warning");

        Assert.Single(comparison.Values);
        Assert.Equal("Error", comparison.Values[0]);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new FilterComparison
        {
            Property = EventProperty.Level,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many,
            Value = "Error",
            Values = ["Error", "Warning"]
        };

        var roundTripped = FilterComparisonDraft.FromComparison(original).ToComparison();

        Assert.Equal(original.Property, roundTripped.Property);
        Assert.Equal(original.Operator, roundTripped.Operator);
        Assert.Equal(original.MatchMode, roundTripped.MatchMode);
        Assert.Equal(original.Value, roundTripped.Value);
        Assert.Equal(original.Values, roundTripped.Values);
    }

    [Fact]
    public void ToComparison_DoesNotShareValuesListWithDraft()
    {
        var draft = new FilterComparisonDraft
        {
            Property = EventProperty.Level,
            Values = ["Error"]
        };

        var comparison = draft.ToComparison();

        draft.Values.Add("Warning");

        Assert.Single(comparison.Values);
        Assert.Equal("Error", comparison.Values[0]);
    }
}

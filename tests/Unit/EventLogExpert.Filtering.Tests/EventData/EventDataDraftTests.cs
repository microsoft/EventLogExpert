// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     <see cref="FilterPredicateDraft.IsComplete" /> must mirror the formatter's strict-mode guard: an EventData row
///     is only complete once it has both a field name and a value, so the editor's Done/Add affordance never enables a row
///     the formatter would silently reject (R3).
/// </summary>
public sealed class EventDataDraftTests
{
    [Fact]
    public void IsComplete_EventDataMany_RequiresFieldName()
    {
        var draft = new FilterPredicateDraft
        {
            Comparison = new FilterComparisonDraft
            {
                Property = EventProperty.EventData,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = ["admin"]
            }
        };

        Assert.False(draft.IsComplete);

        draft.Comparison.EventDataFieldName = "User";

        Assert.True(draft.IsComplete);
    }

    [Fact]
    public void IsComplete_False_WhenFieldNameMissing() => Assert.False(EventDataDraft(null, "admin").IsComplete);

    [Fact]
    public void IsComplete_False_WhenFieldNameWhitespace() => Assert.False(EventDataDraft("   ", "admin").IsComplete);

    [Fact]
    public void IsComplete_False_WhenValueMissing() => Assert.False(EventDataDraft("User", null).IsComplete);

    [Fact]
    public void IsComplete_NonEventDataRow_IgnoresFieldName()
    {
        var draft = new FilterPredicateDraft
        {
            Comparison = new FilterComparisonDraft
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "x"
            }
        };

        Assert.True(draft.IsComplete);
    }

    [Fact]
    public void IsComplete_True_WhenFieldNameAndValuePresent() => Assert.True(EventDataDraft("User", "admin").IsComplete);

    private static FilterPredicateDraft EventDataDraft(string? fieldName, string? value) =>
        new()
        {
            Comparison = new FilterComparisonDraft
            {
                Property = EventProperty.EventData,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                EventDataFieldName = fieldName,
                Value = value
            }
        };
}

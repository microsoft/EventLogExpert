// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests.Basic;

public sealed class BasicFilterTests
{
    [Fact]
    public void HasEmptyMultiContainsComparison_EveryComparisonKeepsValues_ReturnsFalse()
    {
        var root = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Many,
            Values = ["a", ""]
        };
        var predicate = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.NotContains,
            MatchMode = MatchMode.Many,
            Values = ["", "b"]
        };
        var filter = new BasicFilter(root, [new FilterPredicate(predicate, false)]);

        Assert.False(filter.WithNormalizedValues().HasEmptyMultiContainsComparison());
    }

    [Fact]
    public void HasEmptyMultiContainsComparison_PredicateEmptiesOut_ReturnsTrue()
    {
        var root = new FilterComparison
        {
            Property = EventProperty.Id,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = "4624"
        };
        var predicate = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Many,
            Values = [""]
        };
        var filter = new BasicFilter(root, [new FilterPredicate(predicate, false)]);

        Assert.True(filter.WithNormalizedValues().HasEmptyMultiContainsComparison());
    }

    [Fact]
    public void HasEmptyMultiContainsComparison_RootEmptiesOut_ReturnsTrue()
    {
        var root = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Many,
            Values = [""]
        };
        var filter = new BasicFilter(root, ImmutableList<FilterPredicate>.Empty);

        Assert.True(filter.WithNormalizedValues().HasEmptyMultiContainsComparison());
    }

    [Fact]
    public void WithNormalizedValues_NormalizesRootAndEveryPredicate()
    {
        var root = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Many,
            Values = ["a", ""]
        };
        var predicate = new FilterComparison
        {
            Property = EventProperty.EventData,
            EventDataFieldName = "X",
            Operator = ComparisonOperator.NotContains,
            MatchMode = MatchMode.Many,
            Values = ["", "b"]
        };
        var filter = new BasicFilter(root, [new FilterPredicate(predicate, false)]);

        var normalized = filter.WithNormalizedValues();

        Assert.Equal<IEnumerable<string>>(["a"], normalized.Comparison.Values);
        Assert.Equal<IEnumerable<string>>(["b"], normalized.Predicates[0].Comparison.Values);
    }

    [Fact]
    public void WithNormalizedValues_NothingToStrip_ReturnsSameInstance()
    {
        var filter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "x"
            },
            ImmutableList<FilterPredicate>.Empty);

        Assert.Same(filter, filter.WithNormalizedValues());
    }

    [Fact]
    public void WithNormalizedValues_PreservesEqualsEmptyValue()
    {
        var root = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many,
            Values = ["a", ""]
        };
        var filter = new BasicFilter(root, ImmutableList<FilterPredicate>.Empty);

        // Nothing to strip (Equals empties are valid) -> same instance.
        Assert.Same(filter, filter.WithNormalizedValues());
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests.Basic;

public sealed class BasicFilterFormatterTests
{
    [Fact]
    public void TryFormat_WhenLenientAndAnySubFilterInvalid_ShouldSkipInvalidAndReturnTrue()
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "   "
                    },
                    false)
            ]);

        var result = BasicFilterFormatter.TryFormat(source, out var formatted);

        Assert.True(result);
        Assert.Equal("Id == 100", formatted);
    }

    [Fact]
    public void TryFormat_WhenStrictSubFiltersAndAllSubFiltersValid_ShouldReturnTrue()
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    false)
            ]);

        var result = BasicFilterFormatter.TryFormat(source, true, out var formatted);

        Assert.True(result);
        Assert.Equal("Id == 100 && Level == \"Error\"", formatted);
    }

    [Fact]
    public void TryFormat_WhenStrictSubFiltersAndAnySubFilterInvalid_ShouldReturnFalse()
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "   "
                    },
                    false)
            ]);

        var result = BasicFilterFormatter.TryFormat(source, true, out var formatted);

        Assert.False(result);
        Assert.Equal(string.Empty, formatted);
    }

    [Fact]
    public void TryFormat_WhenStrictSubFiltersAndNoSubFilters_ShouldBehaveSameAsLenient()
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Contains,
                MatchMode = MatchMode.Single,
                Value = "Kernel"
            },
            ImmutableList<FilterPredicate>.Empty);

        var strictResult = BasicFilterFormatter.TryFormat(source, true, out var strict);
        var lenientResult = BasicFilterFormatter.TryFormat(source, out var lenient);

        Assert.True(strictResult);
        Assert.True(lenientResult);
        Assert.Equal(strict, lenient);
    }
}

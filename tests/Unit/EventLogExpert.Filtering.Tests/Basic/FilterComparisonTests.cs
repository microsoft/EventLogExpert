// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Tests.Basic;

public sealed class FilterComparisonTests
{
    [Fact]
    public void Default_ShouldHaveEmptyValuesAndNullValue()
    {
        var comparison = new FilterComparison();

        Assert.Null(comparison.Value);
        Assert.Empty(comparison.Values);
    }

    [Fact]
    public void WithField_ShouldNotMutateOriginal()
    {
        var original = new FilterComparison
        {
            Property = EventProperty.Source,
            Value = "100",
            Values = ["200"]
        };

        _ = original.WithProperty(EventProperty.Id);

        Assert.Equal(EventProperty.Source, original.Property);
        Assert.Equal("100", original.Value);
        Assert.Single(original.Values);
    }

    [Fact]
    public void WithField_WhenCalled_ShouldReturnNewInstanceWithClearedValueAndValues()
    {
        var original = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Single,
            Value = "100",
            Values = ["200", "300"]
        };

        var updated = original.WithProperty(EventProperty.Id);

        Assert.NotSame(original, updated);
        Assert.Equal(EventProperty.Id, updated.Property);
        Assert.Null(updated.Value);
        Assert.Empty(updated.Values);
        Assert.Equal(ComparisonOperator.Contains, updated.Operator);
        Assert.Equal(MatchMode.Single, updated.MatchMode);
    }

    [Fact]
    public void WithNormalizedValues_ManyContainsAllEmpty_ProducesEmptyList()
    {
        var comparison = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Many,
            Values = ["", null!, ""]
        };

        Assert.Empty(comparison.WithNormalizedValues().Values);
    }

    [Fact]
    public void WithNormalizedValues_ManyContainsNoEmpty_ReturnsSameInstance()
    {
        var comparison = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Many,
            Values = ["a", " ", "b"]
        };

        Assert.Same(comparison, comparison.WithNormalizedValues());
    }

    [Theory]
    [InlineData(ComparisonOperator.Contains)]
    [InlineData(ComparisonOperator.NotContains)]
    public void WithNormalizedValues_ManyContainsOrNotContains_DropsNullAndEmptyButKeepsWhitespace(ComparisonOperator op)
    {
        var comparison = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = op,
            MatchMode = MatchMode.Many,
            Values = ["a", "", " ", null!, "\t", "b"]
        };

        var normalized = comparison.WithNormalizedValues();

        // "" and null are degenerate (F.Contains("") is always true); a literal space and tab are valid substring searches.
        Assert.NotSame(comparison, normalized);
        Assert.Equal<IEnumerable<string>>(["a", " ", "\t", "b"], normalized.Values);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equals)]
    [InlineData(ComparisonOperator.NotEqual)]
    public void WithNormalizedValues_ManyEqualsOrNotEqual_PreservesEmptyValue(ComparisonOperator op)
    {
        var comparison = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = op,
            MatchMode = MatchMode.Many,
            Values = ["a", "", "b"]
        };

        // Equals/NotEqual with an empty value matches an empty-valued field: preserved, and the instance is unchanged.
        Assert.Same(comparison, comparison.WithNormalizedValues());
    }

    [Fact]
    public void WithNormalizedValues_SingleMode_ReturnsSameInstance()
    {
        var comparison = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Single,
            Value = ""
        };

        Assert.Same(comparison, comparison.WithNormalizedValues());
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Tests;

public sealed class BasicFilterConditionTests
{
    [Fact]
    public void Default_ShouldHaveEmptyValuesAndNullValue()
    {
        var condition = new BasicFilterCondition();

        Assert.Null(condition.Value);
        Assert.Empty(condition.Values);
    }

    [Fact]
    public void WithField_ShouldNotMutateOriginal()
    {
        var original = new BasicFilterCondition
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
        var original = new BasicFilterCondition
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
}

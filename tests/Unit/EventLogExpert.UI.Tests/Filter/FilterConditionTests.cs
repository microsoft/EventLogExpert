// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class FilterConditionTests
{
    [Fact]
    public void Default_ShouldHaveEmptyValuesAndNullValue()
    {
        var condition = new FilterCondition();

        Assert.Null(condition.Value);
        Assert.Empty(condition.Values);
    }

    [Fact]
    public void WithCategory_ShouldNotMutateOriginal()
    {
        var original = new FilterCondition
        {
            Category = FilterCategory.Source,
            Value = Constants.FilterValue100,
            Values = [Constants.FilterValue200]
        };

        _ = original.WithCategory(FilterCategory.Id);

        Assert.Equal(FilterCategory.Source, original.Category);
        Assert.Equal(Constants.FilterValue100, original.Value);
        Assert.Single(original.Values);
    }

    [Fact]
    public void WithCategory_WhenCalled_ShouldReturnNewInstanceWithClearedValueAndValues()
    {
        var original = new FilterCondition
        {
            Category = FilterCategory.Source,
            Evaluator = FilterEvaluator.Contains,
            Value = Constants.FilterValue100,
            Values = [Constants.FilterValue200, Constants.FilterValue300]
        };

        var updated = original.WithCategory(FilterCategory.Id);

        Assert.NotSame(original, updated);
        Assert.Equal(FilterCategory.Id, updated.Category);
        Assert.Null(updated.Value);
        Assert.Empty(updated.Values);
        Assert.Equal(FilterEvaluator.Contains, updated.Evaluator);
    }
}

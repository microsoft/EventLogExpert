// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Models;

public sealed class BasicFilterCriteriaTests
{
    [Fact]
    public void Default_ShouldHaveEmptyValuesAndNullValue()
    {
        var criteria = new BasicFilterCriteria();

        Assert.Null(criteria.Value);
        Assert.Empty(criteria.Values);
    }

    [Fact]
    public void WithCategory_ShouldNotMutateOriginal()
    {
        var original = new BasicFilterCriteria
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
        var original = new BasicFilterCriteria
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

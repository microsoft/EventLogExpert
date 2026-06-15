// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Scenarios.Tests;

public sealed class BuiltInScenarioRegistryTests
{
    [Fact]
    public void BuildFilterSet_ProducesOneCompiledBasicFilterPerRow()
    {
        var registry = new BuiltInScenarioRegistry([new FakeSource(Make("a", "1000", "1001"))]);

        var filterSet = registry.BuildFilterSet(registry.Scenarios[0]);

        Assert.Equal(2, filterSet.Count);
        Assert.All(filterSet, saved =>
        {
            Assert.NotNull(saved.Compiled);
            Assert.Equal(FilterMode.Basic, saved.Mode);
            Assert.NotNull(saved.BasicFilter);
        });
    }

    [Fact]
    public void Registry_AggregatesAllSources()
    {
        var registry = new BuiltInScenarioRegistry([new FakeSource(Make("a")), new FakeSource(Make("b"))]);

        Assert.Equal(["a", "b"], registry.Scenarios.Select(scenario => scenario.Id));
    }

    [Fact]
    public void Registry_DuplicateIdAcrossSources_Throws()
    {
        var exception = Assert.Throws<ScenarioCatalogException>(() =>
            new BuiltInScenarioRegistry([new FakeSource(Make("dup")), new FakeSource(Make("dup"))]));

        Assert.Contains(exception.Errors, error => error.Contains("more than one source"));
    }

    private static ScenarioDefinition Make(string id, params string[] ids)
    {
        var values = ids.Length == 0 ? ["1000"] : ids;

        return new ScenarioDefinition
        {
            Id = id,
            Name = id,
            Purpose = "p",
            Group = ScenarioGroup.SystemHealth,
            Channels = ["System"],
            Filters =
            [
                .. values.Select(value => new ScenarioFilterRow(
                    new BasicFilter(
                        new FilterComparison
                        {
                            Property = EventProperty.Id,
                            Operator = ComparisonOperator.Equals,
                            MatchMode = MatchMode.Single,
                            Value = value
                        },
                        [])))
            ]
        };
    }

    private sealed class FakeSource(params ScenarioDefinition[] scenarios) : IScenarioSource
    {
        public IReadOnlyList<ScenarioDefinition> GetScenarios() => scenarios;
    }
}

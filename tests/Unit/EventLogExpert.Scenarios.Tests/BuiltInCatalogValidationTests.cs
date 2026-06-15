// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Scenarios.Tests;

public sealed class BuiltInCatalogValidationTests
{
    private static readonly BuiltInScenarioRegistry s_registry = new([new BuiltInScenarioSource()]);

    public static TheoryData<string> AllScenarioIds()
    {
        var data = new TheoryData<string>();

        foreach (var scenario in s_registry.Scenarios) { data.Add(scenario.Id); }

        return data;
    }

    [Fact]
    public void Catalog_IdsAreUniqueAndKebabCase()
    {
        var ids = s_registry.Scenarios.Select(scenario => scenario.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", id));
    }

    [Fact]
    public void Catalog_LoadsWithScenarios()
    {
        Assert.NotEmpty(s_registry.Scenarios);
    }

    [Fact]
    public void Catalog_StableGuidsAreUnique()
    {
        var guids = s_registry.Scenarios.Select(scenario => scenario.StableGuid).ToList();

        Assert.Equal(guids.Count, guids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, guids);
    }

    [Fact]
    public void CombinedScenarios_RowsAreLogNameScoped()
    {
        var combined = s_registry.Scenarios.Where(scenario => scenario.Channels.Length > 1).ToList();

        Assert.All(combined, scenario => Assert.All(scenario.Filters, row =>
            Assert.True(
                row.Filter.Comparison.Property is EventProperty.LogName ||
                row.Filter.Predicates.Any(predicate => predicate.Comparison.Property is EventProperty.LogName),
                $"Combined scenario '{scenario.Id}' has a row that is not LogName-scoped.")));
    }

    [Theory]
    [MemberData(nameof(AllScenarioIds))]
    public void Scenario_FilterSet_CompilesCanonicalAndBasic(string id)
    {
        var scenario = s_registry.Scenarios.Single(scenario => scenario.Id == id);

        var filterSet = s_registry.BuildFilterSet(scenario);

        Assert.Equal(scenario.Filters.Length, filterSet.Count);

        Assert.All(filterSet, saved =>
        {
            Assert.NotNull(saved.Compiled);
            Assert.Equal(FilterMode.Basic, saved.Mode);

            var roundTripped = SavedFilter.TryCreate(saved.ComparisonText, basicFilter: null, mode: FilterMode.Basic);

            Assert.NotNull(roundTripped?.BasicFilter);
            Assert.True(BasicFilterFormatter.TryFormat(roundTripped.BasicFilter, strictPredicates: true, out var reformatted));
            Assert.Equal(saved.ComparisonText, reformatted);
        });
    }

    [Theory]
    [MemberData(nameof(AllScenarioIds))]
    public void Scenario_RequiresAdmin_ConsistentWithChannels(string id)
    {
        var scenario = s_registry.Scenarios.Single(scenario => scenario.Id == id);

        if (scenario.Channels.Any(LogChannelNames.AdminOnlyLiveChannels.Contains))
        {
            Assert.True(scenario.RequiresAdmin);
        }
    }
}

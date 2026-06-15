// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
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

    [Fact]
    public void DescriptionContains_MatchesRealisticEvent()
    {
        var officeSet = s_registry.BuildFilterSet(s_registry.Scenarios.Single(scenario => scenario.Id == "office-crashes-hangs"));
        var wordCrash = new ResolvedEvent("Application", LogPathType.Channel)
        {
            LogName = "Application",
            Source = "Application Error",
            Id = 1000,
            Description = "Faulting application name: WINWORD.EXE, version: 16.0.1, time stamp: 0x0"
        };
        Assert.Contains(officeSet, saved => saved.Compiled!.Predicate(wordCrash));

        var lolbinSet = s_registry.BuildFilterSet(s_registry.Scenarios.Single(scenario => scenario.Id == "suspicious-lolbin-execution"));
        var processCreate = new ResolvedEvent("Security", LogPathType.Channel)
        {
            LogName = "Security",
            Source = "Microsoft-Windows-Security-Auditing",
            Id = 4688,
            Description = "A new process has been created. New Process Name: C:\\Windows\\System32\\rundll32.exe"
        };
        Assert.Contains(lolbinSet, saved => saved.Compiled!.Predicate(processCreate));
    }

    [Fact]
    public void MultiSourceOrRow_MatchesEachOrTerm()
    {
        var scenario = s_registry.Scenarios.Single(scenario => scenario.Id == "storage-controller-driver-resets");
        var saved = s_registry.BuildFilterSet(scenario).Single();
        Assert.NotNull(saved.Compiled);

        static ResolvedEvent Event(string source, int id) =>
            new("System", LogPathType.Channel) { LogName = "System", Source = source, Id = id };

        Assert.True(saved.Compiled!.Predicate(Event("storahci", 11)), "storahci term should match");
        Assert.True(saved.Compiled!.Predicate(Event("stornvme", 14)), "stornvme term should match");
        Assert.True(saved.Compiled!.Predicate(Event("iaStorA", 129)), "iaStorA term should match");
        Assert.True(saved.Compiled!.Predicate(Event("disk", 153)), "disk term should match");
        Assert.False(saved.Compiled!.Predicate(Event("disk", 11)), "disk with a non-matching id should not match");
        Assert.False(saved.Compiled!.Predicate(Event("storahci", 999)), "storahci with a non-matching id should not match");
        Assert.False(saved.Compiled!.Predicate(Event("Disk", 153)), "Source matching is case-sensitive (Ordinal): the real provider is 'disk', so 'Disk' must not match");
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

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ScenarioQueryService(BuiltInScenarioRegistry registry) : IScenarioQueryService
{
    private readonly BuiltInScenarioRegistry _registry = registry;

    public IReadOnlyList<ScenarioDefinition> GetInAppScenarios(IReadOnlyCollection<string> loadedLogNames)
    {
        ArgumentNullException.ThrowIfNull(loadedLogNames);

        var loaded = loadedLogNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.._registry.Scenarios.Where(scenario => scenario.Channels.Any(loaded.Contains))];
    }

    public IReadOnlyList<ScenarioDefinition> GetSplashScenarios() =>
    [
        .._registry.Scenarios.Where(scenario => scenario.Gating == ScenarioGating.ChannelPresence)
    ];
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;
using System.Collections.Frozen;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ScenarioQueryService(BuiltInScenarioRegistry registry, IChannelPresenceProbe presenceProbe)
    : IScenarioQueryService
{
    private readonly IChannelPresenceProbe _presenceProbe = presenceProbe;
    private readonly BuiltInScenarioRegistry _registry = registry;

    public IReadOnlyList<ScenarioDefinition> GetInAppScenarios(IReadOnlyCollection<string> loadedLogNames)
    {
        ArgumentNullException.ThrowIfNull(loadedLogNames);

        var loaded = loadedLogNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.._registry.Scenarios.Where(scenario => scenario.Channels.Any(loaded.Contains))];
    }

    public LivePresence GetLivePresence()
    {
        var present = _presenceProbe.TryGetPresentChannels();

        return present is null
            ? new LivePresence(false, FrozenSet<string>.Empty)
            : new LivePresence(true, present);
    }

    public IReadOnlyList<ScenarioDefinition> GetSplashScenarios() =>
    [
        .._registry.Scenarios.Where(scenario => scenario.Gating == ScenarioGating.ChannelPresence)
    ];
}

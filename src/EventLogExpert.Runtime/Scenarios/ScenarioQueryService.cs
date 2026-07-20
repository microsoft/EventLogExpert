// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;
using System.Collections.Frozen;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ScenarioQueryService(BuiltInScenarioRegistry registry, IChannelReadinessService readinessService)
    : IScenarioQueryService
{
    private readonly IChannelReadinessService _readinessService = readinessService;
    private readonly BuiltInScenarioRegistry _registry = registry;

    public IReadOnlyList<ScenarioDefinition> GetInAppScenarios(IReadOnlyCollection<string> loadedLogNames)
    {
        ArgumentNullException.ThrowIfNull(loadedLogNames);

        var loaded = loadedLogNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.._registry.Scenarios.Where(scenario => scenario.Channels.Any(loaded.Contains))];
    }

    public async Task<LivePresence> GetLivePresenceAsync()
    {
        var readiness = await _readinessService.GetReadinessAsync(CatalogChannels());

        return readiness.Any(channel => channel.Presence == ChannelPresence.Unknown)
            ? new LivePresence(false, FrozenSet<string>.Empty)
            : new LivePresence(
                true,
                readiness
                    .Where(channel => channel.Presence == ChannelPresence.Present)
                    .Select(channel => channel.Channel)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ScenarioDefinition> GetSplashScenarios() =>
    [
        .._registry.Scenarios.Where(scenario => scenario.Gating == ScenarioGating.ChannelPresence)
    ];

    private IEnumerable<string> CatalogChannels() =>
        _registry.Scenarios
            .SelectMany(static scenario => scenario.Channels.Concat(scenario.OptionalChannels))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}

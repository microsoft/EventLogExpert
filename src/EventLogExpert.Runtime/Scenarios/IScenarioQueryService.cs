// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>Selects which scenarios to surface on the splash dashboard and in-app.</summary>
public interface IScenarioQueryService
{
    /// <summary>Scenarios whose channels match a currently-loaded log name.</summary>
    IReadOnlyList<ScenarioDefinition> GetInAppScenarios(IReadOnlyCollection<string> loadedLogNames);

    /// <summary>Every channel-presence scenario in the catalog, regardless of local availability.</summary>
    IReadOnlyList<ScenarioDefinition> GetSplashScenarios();
}

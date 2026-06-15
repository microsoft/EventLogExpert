// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>A source of <see cref="ScenarioDefinition" />s aggregated by the registry.</summary>
public interface IScenarioSource
{
    IReadOnlyList<ScenarioDefinition> GetScenarios();
}

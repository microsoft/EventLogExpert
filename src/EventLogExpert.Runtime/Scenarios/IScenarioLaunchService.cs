// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>Launches a scenario: applies its filter set and opens its channels.</summary>
public interface IScenarioLaunchService
{
    /// <summary>
    ///     Applies the scenario's filters, then opens its channels. A null <paramref name="dateWindow" /> clears the date
    ///     filter; <paramref name="combineLog" /> false opens a fresh view, true merges into the active workspace.
    /// </summary>
    Task<ScenarioLaunchResult> LaunchAsync(ScenarioDefinition scenario, DateFilter? dateWindow, bool combineLog = false);
}

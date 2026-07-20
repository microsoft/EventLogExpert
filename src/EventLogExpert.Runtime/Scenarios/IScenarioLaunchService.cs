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

    /// <summary>
    ///     Prompts for a folder, opens the exported <c>.evtx</c> files whose channel matches the scenario, and applies
    ///     the scenario's filters to a fresh view. The scenario's filters and channels are read from the definition, so this
    ///     works even for logs not present on the local host. The folder enumeration and per-file channel probe run off the
    ///     caller's thread and honor <paramref name="cancellationToken" />, returning
    ///     <see cref="ScenarioFolderOutcome.Cancelled" /> if the scan is abandoned before any log is opened.
    /// </summary>
    Task<ScenarioFolderLaunchResult> LaunchFromFolderAsync(
        ScenarioDefinition scenario, DateFilter? dateWindow, CancellationToken cancellationToken = default);
}

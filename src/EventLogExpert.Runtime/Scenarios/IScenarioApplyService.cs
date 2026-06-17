// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>Applies a built-in scenario's filter rows to the pane in-app, without opening any log.</summary>
public interface IScenarioApplyService
{
    /// <summary>
    ///     Merges (append) or, when <paramref name="replace" /> is true, replaces the pane's filter rows with the
    ///     scenario's rows.
    /// </summary>
    void ApplyInApp(ScenarioDefinition scenario, bool replace);
}

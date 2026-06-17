// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>Exports live Basic filter rows from the filter pane into scenario-catalog JSON (dev authoring aid).</summary>
public interface IScenarioAuthoringService
{
    /// <summary>
    ///     Resolves each saved filter to a Basic filter (directly, or by decomposing its expression) and exports the
    ///     resolvable rows as one scenario, scoped to <paramref name="channels" />. Rows that cannot be expressed as a Basic
    ///     filter are skipped and reported in the result's warnings.
    /// </summary>
    ScenarioExportResult ExportRows(IReadOnlyList<SavedFilter> rows, IReadOnlyList<string> channels);
}

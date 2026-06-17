// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ScenarioAuthoringService : IScenarioAuthoringService
{
    public ScenarioExportResult ExportRows(IReadOnlyList<SavedFilter> rows, IReadOnlyList<string> channels)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(channels);

        var exportRows = new List<ScenarioExportRow>(rows.Count);
        var skipped = 0;

        foreach (var row in rows)
        {
            var basicFilter = row.BasicFilter
                ?? SavedFilter.TryCreate(row.ComparisonText, mode: FilterMode.Basic)?.BasicFilter;

            if (basicFilter is null)
            {
                skipped++;

                continue;
            }

            exportRows.Add(new ScenarioExportRow(basicFilter, row.IsExcluded, row.Color));
        }

        var result = ScenarioExporter.Export(
            exportRows,
            new ScenarioExportMetadata(Id: null, Name: null, Purpose: null, Group: null, channels));

        return skipped == 0
            ? result
            : result with { Warnings = result.Warnings.Add($"{skipped} row(s) skipped: not expressible as a Basic filter.") };
    }
}

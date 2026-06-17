// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>One row to export: its Basic filter, exclusion flag, and highlight color.</summary>
public sealed record ScenarioExportRow(BasicFilter Filter, bool IsExcluded, HighlightColor Color)
{
    public BasicFilter Filter { get; init; } = Filter ?? throw new ArgumentNullException(nameof(Filter));
}

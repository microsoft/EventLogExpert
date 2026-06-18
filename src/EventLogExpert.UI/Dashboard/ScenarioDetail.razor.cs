// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Dashboard;

public sealed partial class ScenarioDetail
{
    private readonly string _nameId = ComponentId.NewUnique().Value;

    [Parameter][EditorRequired] public string ElevationReasonId { get; set; } = string.Empty;

    [Parameter] public bool IsBusy { get; set; }

    [Parameter] public bool IsDisabled { get; set; }

    [Parameter] public bool IsFavored { get; set; }

    [Parameter] public EventCallback OnLaunch { get; set; }

    [Parameter] public EventCallback OnToggleFavorite { get; set; }

    [Parameter][EditorRequired] public ScenarioDefinition Scenario { get; set; } = null!;

    private IReadOnlyList<FilterLine> FilterLines
    {
        get
        {
            if (Scenario.Filters.IsDefaultOrEmpty) { return []; }

            List<FilterLine> lines = [];

            foreach (var row in Scenario.Filters)
            {
                if (!BasicFilterFormatter.TryFormat(row.Filter, out var text)) { continue; }

                lines.Add(new FilterLine(row.IsExcluded ? $"Exclude {text}" : text, row.Color));
            }

            return lines;
        }
    }

    private async Task LaunchAsync()
    {
        if (IsDisabled) { return; }

        await OnLaunch.InvokeAsync();
    }

    private readonly record struct FilterLine(string Text, HighlightColor Color);
}

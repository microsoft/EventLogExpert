// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Inputs;

public sealed partial class DateRangeQuickPick : ComponentBase
{
    private static readonly IReadOnlyList<DateRangePreset> s_presets =
    [
        new("7d", "Last 7 days", now => now.AddDays(-7)),
        new("14d", "Last 14 days", now => now.AddDays(-14)),
        new("1mo", "Last 1 month", now => now.AddMonths(-1)),
        new("3mo", "Last 3 months", now => now.AddMonths(-3)),
        new("6mo", "Last 6 months", now => now.AddMonths(-6)),
        new("1yr", "Last 1 year", now => now.AddYears(-1)),
        new("2yr", "Last 2 years", now => now.AddYears(-2)),
    ];

    private string _selectedKey = string.Empty;

    [Parameter] public string AriaLabel { get; set; } = "Quick date range";

    [Parameter] public string CssClass { get; set; } = string.Empty;

    [Parameter] public Func<DateTime> NowUtc { get; set; } = () => DateTime.UtcNow;

    [Parameter] public EventCallback<DateFilter> OnRangeSelected { get; set; }

    [Parameter] public string PlaceholderText { get; set; } = "Quick range...";

    private async Task OnPresetSelectedAsync(string key)
    {
        _selectedKey = key;

        var preset = s_presets.FirstOrDefault(candidate => candidate.Key == key);

        if (preset is null) { return; }

        var now = NowUtc();
        var dateFilter = new DateFilter { After = preset.ComputeAfter(now), Before = now };

        await OnRangeSelected.InvokeAsync(dateFilter);
    }

    private string PresetLabel(string? key) =>
        s_presets.FirstOrDefault(preset => preset.Key == key)?.Label ?? PlaceholderText;

    private sealed record DateRangePreset(string Key, string Label, Func<DateTime, DateTime> ComputeAfter);
}

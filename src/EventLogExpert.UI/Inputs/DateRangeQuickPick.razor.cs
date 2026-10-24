// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Inputs;

/// <summary>Raises a preset date range as UTC instants, leaving the end open for relative presets.</summary>
public sealed partial class DateRangeQuickPick : ComponentBase
{
    private const string EntireLogKey = "all";

    private static readonly IReadOnlyList<DateRangePreset> s_relativePresets =
    [
        new("15m", "DateRangeQuickPick_Last15Minutes", now => now.AddMinutes(-15)),
        new("1h", "DateRangeQuickPick_Last1Hour", now => now.AddHours(-1)),
        new("24h", "DateRangeQuickPick_Last24Hours", now => now.AddHours(-24)),
        new("7d", "DateRangeQuickPick_Last7Days", now => now.AddDays(-7)),
        new("14d", "DateRangeQuickPick_Last14Days", now => now.AddDays(-14)),
        new("1mo", "DateRangeQuickPick_Last1Month", now => now.AddMonths(-1)),
        new("3mo", "DateRangeQuickPick_Last3Months", now => now.AddMonths(-3)),
        new("6mo", "DateRangeQuickPick_Last6Months", now => now.AddMonths(-6)),
        new("1yr", "DateRangeQuickPick_Last1Year", now => now.AddYears(-1)),
        new("2yr", "DateRangeQuickPick_Last2Years", now => now.AddYears(-2)),
    ];

    private string _selectedKey = string.Empty;

    [Parameter][EditorRequired] public string AriaLabel { get; set; } = string.Empty;

    [Parameter] public string CssClass { get; set; } = string.Empty;

    /// <summary>Supplies the UTC range of the loaded events; when set, an "Entire log" option is offered.</summary>
    [Parameter] public Func<(DateTime After, DateTime Before)>? EntireLogRangeUtc { get; set; }

    [Parameter] public Func<DateTime> NowUtc { get; set; } = () => DateTime.UtcNow;

    [Parameter] public EventCallback<DateFilter> OnRangeSelected { get; set; }

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private async Task OnPresetSelectedAsync(string key)
    {
        _selectedKey = key;

        if (key == EntireLogKey && EntireLogRangeUtc is { } entireLogRange)
        {
            var (after, before) = entireLogRange();

            await OnRangeSelected.InvokeAsync(new DateFilter { After = after, Before = before });

            return;
        }

        var preset = s_relativePresets.FirstOrDefault(candidate => candidate.Key == key);

        if (preset is null) { return; }

        await OnRangeSelected.InvokeAsync(new DateFilter { After = preset.ComputeAfter(NowUtc()) });
    }

    private string PresetLabel(string? key)
    {
        if (key == EntireLogKey) { return Localizer["DateRangeQuickPick_EntireLog"]; }

        var preset = s_relativePresets.FirstOrDefault(candidate => candidate.Key == key);

        return preset is null ? Localizer["DateRangeQuickPick_Placeholder"] : Localizer[preset.LabelKey];
    }

    private sealed record DateRangePreset(string Key, string LabelKey, Func<DateTime, DateTime> ComputeAfter);
}

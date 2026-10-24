// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class DateRangeQuickPickTests : BunitContext
{
    private static readonly DateTime s_leapNow = new(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime s_standardNow = new(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public DateRangeQuickPickTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddEventLogLocalization();
    }

    public static IEnumerable<object[]> PresetCases() =>
    [
        [s_standardNow, "Last 15 minutes", new DateTime(2024, 6, 15, 11, 45, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 1 hour", new DateTime(2024, 6, 15, 11, 0, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 24 hours", new DateTime(2024, 6, 14, 12, 0, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 7 days", new DateTime(2024, 6, 8, 12, 0, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 14 days", new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 1 month", new DateTime(2024, 5, 15, 12, 0, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 3 months", new DateTime(2024, 3, 15, 12, 0, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 6 months", new DateTime(2023, 12, 15, 12, 0, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 1 year", new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Utc)],
        [s_standardNow, "Last 2 years", new DateTime(2022, 6, 15, 12, 0, 0, DateTimeKind.Utc)],

        // Leap-day source: AddMonths/AddYears clamp to the nearest valid day.
        [s_leapNow, "Last 1 month", new DateTime(2024, 1, 29, 12, 0, 0, DateTimeKind.Utc)],
        [s_leapNow, "Last 6 months", new DateTime(2023, 8, 29, 12, 0, 0, DateTimeKind.Utc)],
        [s_leapNow, "Last 1 year", new DateTime(2023, 2, 28, 12, 0, 0, DateTimeKind.Utc)],
    ];

    [Fact]
    public async Task ArrowDown_NavigatesToSuccessivePresets()
    {
        var captured = new List<DateFilter>();
        var component = Render<DateRangeQuickPick>(parameters => parameters
            .Add(p => p.NowUtc, () => s_standardNow)
            .Add(p => p.OnRangeSelected, EventCallback.Factory.Create<DateFilter>(this, captured.Add)));

        var dropdown = component.Find(".dropdown-input");

        // A collapsed combobox opens on the first ArrowDown without committing a preset;
        // each subsequent press then advances to the next preset.
        await dropdown.KeyDownAsync(new KeyboardEventArgs { Code = "ArrowDown" });
        Assert.Empty(captured);

        await dropdown.KeyDownAsync(new KeyboardEventArgs { Code = "ArrowDown" });
        await dropdown.KeyDownAsync(new KeyboardEventArgs { Code = "ArrowDown" });

        Assert.Equal(2, captured.Count);
        Assert.Equal(new DateTime(2024, 6, 15, 11, 45, 0, DateTimeKind.Utc), captured[0].After);
        Assert.Equal(new DateTime(2024, 6, 15, 11, 0, 0, DateTimeKind.Utc), captured[1].After);
    }

    [Fact]
    public void Render_AppliesAriaLabel()
    {
        var component = Render<DateRangeQuickPick>(parameters => parameters
            .Add(p => p.AriaLabel, "Quick date range"));

        Assert.Equal("Quick date range", component.Find("input[role='combobox']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_WithEntireLogRange_OffersEntireLogFirst()
    {
        var component = Render<DateRangeQuickPick>(parameters => parameters
            .Add(p => p.EntireLogRangeUtc, () => (s_leapNow, s_standardNow)));

        var options = component.FindAll("[role='option']");

        Assert.Equal(11, options.Count);
        Assert.Equal("Entire log", options[0].TextContent.Trim());
    }

    [Fact]
    public void Render_WithoutEntireLogRange_ShowsPlaceholderAndOnlyRelativePresets()
    {
        var component = Render<DateRangeQuickPick>();

        Assert.Equal("Quick range...", component.Find("input[role='combobox']").GetAttribute("value"));
        Assert.Equal(10, component.FindAll("[role='option']").Count);
        Assert.DoesNotContain(component.FindAll("[role='option']"), option => option.TextContent.Trim() == "Entire log");
    }

    [Fact]
    public async Task Selecting_EntireLog_EmitsTheSuppliedClosedRange()
    {
        DateFilter? captured = null;
        var component = Render<DateRangeQuickPick>(parameters => parameters
            .Add(p => p.EntireLogRangeUtc, () => (s_leapNow, s_standardNow))
            .Add(p => p.OnRangeSelected, EventCallback.Factory.Create<DateFilter>(this, filter => captured = filter)));

        await SelectPreset(component, "Entire log");

        Assert.NotNull(captured);
        Assert.Equal(s_leapNow, captured!.After);
        Assert.Equal(s_standardNow, captured.Before);
    }

    [Theory]
    [MemberData(nameof(PresetCases))]
    public async Task Selecting_Preset_EmitsUtcStartWithAnOpenEnd(DateTime now, string label, DateTime expectedAfter)
    {
        DateFilter? captured = null;
        var component = Render<DateRangeQuickPick>(parameters => parameters
            .Add(p => p.NowUtc, () => now)
            .Add(p => p.OnRangeSelected, EventCallback.Factory.Create<DateFilter>(this, filter => captured = filter)));

        await SelectPreset(component, label);

        Assert.NotNull(captured);
        Assert.Equal(expectedAfter, captured!.After);
        Assert.Null(captured.Before);
        Assert.Equal(DateTimeKind.Utc, captured.After!.Value.Kind);
        Assert.True(captured.IsEnabled);
    }

    [Fact]
    public async Task Selecting_Preset_ShowsSelectedLabel()
    {
        var component = Render<DateRangeQuickPick>(parameters => parameters
            .Add(p => p.NowUtc, () => s_standardNow)
            .Add(p => p.OnRangeSelected, EventCallback.Factory.Create<DateFilter>(this, _ => { })));

        await SelectPreset(component, "Last 7 days");

        Assert.Equal("Last 7 days", component.Find("input[role='combobox']").GetAttribute("value"));
    }

    [Fact]
    public async Task Selecting_PresetsInSequence_EmitsEachSelection()
    {
        var captured = new List<DateFilter>();
        var component = Render<DateRangeQuickPick>(parameters => parameters
            .Add(p => p.NowUtc, () => s_standardNow)
            .Add(p => p.OnRangeSelected, EventCallback.Factory.Create<DateFilter>(this, captured.Add)));

        await SelectPreset(component, "Last 7 days");
        await SelectPreset(component, "Last 1 month");

        Assert.Equal(2, captured.Count);
        Assert.Equal(new DateTime(2024, 6, 8, 12, 0, 0, DateTimeKind.Utc), captured[0].After);
        Assert.Equal(new DateTime(2024, 5, 15, 12, 0, 0, DateTimeKind.Utc), captured[1].After);
    }

    private static async Task SelectPreset(IRenderedComponent<DateRangeQuickPick> component, string label)
    {
        var option = component.FindAll("[role='option']").Single(item => item.TextContent.Trim() == label);

        await option.MouseDownAsync(new MouseEventArgs());
    }
}

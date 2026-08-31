// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.UI.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;

namespace EventLogExpert.UI.Tests.Localization;

public sealed class HistogramTextComposerTests : IDisposable
{
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public HistogramTextComposerTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        _localizer = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddEventLogLocalization()
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();
    }

    [Fact]
    public void BarTooltip_WithBreakdown_FormatsParenthesizedReverseItemsAndHighlightSuffix()
    {
        // Production supplies the per-item highlight text via Histogram_Highlight_Single ("{color} highlight") and only
        // for DATA groups (CategoricalOther highlights are suppressed in the pane), so mirror that exactly: highlight
        // the Bravo data group with the real "Light red highlight" rendering.
        string highlight = _localizer["Histogram_Highlight_Single", HighlightColorLocalizer.Label(_localizer, HighlightColor.LightRed)].Value;
        IReadOnlyList<HistogramBreakdownItem> items = HistogramTextComposer.GroupBreakdownItems(
            [1, 2, 7],
            Groups(),
            group => group == 2 ? highlight : string.Empty);

        string text = HistogramTextComposer.BarTooltip(
            _localizer,
            total: 1200,
            HistogramEventNoun.Events,
            Start(),
            End(),
            windowCrossesDay: false,
            items);

        Assert.Equal("1200 events (7 Bravo, Light red highlight, 2 Other (1 source), 1 Alpha), 13:45:30 - 14:00:30", text);
    }

    [Theory]
    [InlineData(false, false, "1/1/2024 1:45 PM to 1/1/2024 2:00 PM: 1 events.")]
    [InlineData(true, false, "1/1/2024 1:45 PM to 1/1/2024 2:00 PM: 1 events, spike.")]
    [InlineData(false, true, "1/1/2024 1:45 PM to 1/1/2024 2:00 PM: 1 events (2 Other (1 source), 1 Alpha).")]
    [InlineData(true, true, "1/1/2024 1:45 PM to 1/1/2024 2:00 PM: 1 events (2 Other (1 source), 1 Alpha), spike.")]
    public void BinCursorAnnouncement_UsesTheTemplateForEachSpikeAndBreakdownCombination(
        bool isSpike,
        bool hasBreakdown,
        string expected)
    {
        IReadOnlyList<HistogramBreakdownItem> items = hasBreakdown
            ? HistogramTextComposer.GroupBreakdownItems([1, 2, 0], Groups(), groupHighlightText: null)
            : [];

        string text = HistogramTextComposer.BinCursorAnnouncement(
            _localizer,
            total: 1,
            HistogramEventNoun.Events,
            Start(),
            End(),
            isSpike,
            items);

        Assert.Equal(expected, text);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }

    [Fact]
    public void RegionAria_ErrorCodeEventsSingleCount_RendersPluralErrorCodeEvents()
    {
        string text = HistogramTextComposer.RegionAria(_localizer, total: 1, HistogramEventNoun.ErrorCodeEvents, Start(), End(), []);

        Assert.Equal("Timeline: 1 error-code events from 1/1/2024 1:45 PM to 1/1/2024 2:00 PM.", text);
    }

    [Fact]
    public void RegionAria_FormatsDateTimeWithGeneralShortPatternAndPluralOneCount()
    {
        string text = HistogramTextComposer.RegionAria(
            _localizer,
            total: 1,
            HistogramEventNoun.Events,
            Start(),
            End(),
            []);

        Assert.Equal("Timeline: 1 events from 1/1/2024 1:45 PM to 1/1/2024 2:00 PM.", text);
    }

    [Fact]
    public void RegionAria_WithBreakdown_AppendsCommaSeparatedReverseOrderedItems()
    {
        IReadOnlyList<HistogramBreakdownItem> items =
            HistogramTextComposer.GroupBreakdownItems([1, 2, 7], Groups(), groupHighlightText: null);

        string text = HistogramTextComposer.RegionAria(_localizer, total: 1200, HistogramEventNoun.Events, Start(), End(), items);

        Assert.Equal(
            "Timeline: 1200 events from 1/1/2024 1:45 PM to 1/1/2024 2:00 PM, 7 Bravo, 2 Other (1 source), 1 Alpha.",
            text);
    }

    [Fact]
    public void WindowAnnouncement_FormatsDateTimeWithGeneralShortPatternAndRawLargeCount()
    {
        string text = HistogramTextComposer.WindowAnnouncement(
            _localizer,
            total: 1200,
            HistogramEventNoun.Events,
            Start(),
            End(),
            []);

        Assert.Equal("Showing 1/1/2024 1:45 PM to 1/1/2024 2:00 PM: 1200 events.", text);
    }

    [Fact]
    public void WindowAnnouncement_WithBreakdown_AppendsCommaSeparatedReverseOrderedItems()
    {
        IReadOnlyList<HistogramBreakdownItem> items =
            HistogramTextComposer.GroupBreakdownItems([1, 2, 7], Groups(), groupHighlightText: null);

        string text = HistogramTextComposer.WindowAnnouncement(_localizer, total: 1200, HistogramEventNoun.Events, Start(), End(), items);

        Assert.Equal(
            "Showing 1/1/2024 1:45 PM to 1/1/2024 2:00 PM: 1200 events, 7 Bravo, 2 Other (1 source), 1 Alpha.",
            text);
    }

    private static DateTime End() => new(2024, 1, 1, 14, 0, 30, DateTimeKind.Unspecified);

    private static IReadOnlyList<HistogramGroup> Groups() =>
    [
        new(new HistogramGroupLabel.DataValue("Alpha"), "a", "a", [0]),
        new(new HistogramGroupLabel.CategoricalOther(HistogramDimension.Source, 1), "other", "other", [1]),
        new(new HistogramGroupLabel.DataValue("Bravo"), "b", "b", [2])
    ];

    private static DateTime Start() => new(2024, 1, 1, 13, 45, 30, DateTimeKind.Unspecified);
}

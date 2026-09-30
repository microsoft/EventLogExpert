// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Tests.Localization;

[Collection(CultureSensitiveCollection.Name)]
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

        AssertContainsRawCount(text, 1200);
        AssertCountNounAdjacent(text, 1200, HistogramEventNoun.Events);
        Assert.Contains(Start().ToString("HH:mm:ss", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.Contains(End().ToString("HH:mm:ss", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        AssertBreakdownRendersInReverseOrderWithCounts(text, [1, 2, 7]);
        AssertBreakdownParenthesized(text, [1, 2, 7]);
        Assert.Contains(highlight, text, StringComparison.Ordinal);
    }

    [Fact]
    public void BinCursorAnnouncement_RoutesSpikeAndBreakdownToDistinctStructuralSegments()
    {
        IReadOnlyList<HistogramBreakdownItem> breakdown =
            HistogramTextComposer.GroupBreakdownItems([1, 2, 0], Groups(), groupHighlightText: null);

        string plain = BinCursor(isSpike: false, []);
        string spike = BinCursor(isSpike: true, []);
        string plainBreakdown = BinCursor(isSpike: false, breakdown);
        string spikeBreakdown = BinCursor(isSpike: true, breakdown);

        // Every combination shares the same core (general-short window + count adjacent to the singular noun) and is
        // distinct from the other three, so each (isSpike, hasBreakdown) pair maps to its own template.
        foreach (string rendered in new[] { plain, spike, plainBreakdown, spikeBreakdown })
        {
            AssertContainsGeneralShortWindow(rendered);
            AssertCountNounAdjacent(rendered, 1, HistogramEventNoun.Events);
        }

        Assert.Equal(4, new[] { plain, spike, plainBreakdown, spikeBreakdown }.Distinct(StringComparer.Ordinal).Count());

        // The spike flag adds exactly the marker the resource defines - derived from the templates, never the literal
        // word - present in both spike renders and neither plain render.
        string spikeMarker = SpikeMarkerFromTemplates();
        Assert.Contains(spikeMarker, spike, StringComparison.Ordinal);
        Assert.Contains(spikeMarker, spikeBreakdown, StringComparison.Ordinal);
        Assert.DoesNotContain(spikeMarker, plain, StringComparison.Ordinal);
        Assert.DoesNotContain(spikeMarker, plainBreakdown, StringComparison.Ordinal);

        // The breakdown flag appends the rendered items in reverse group order with their counts; the non-breakdown
        // renders contain none of the group labels.
        AssertBreakdownRendersInReverseOrderWithCounts(plainBreakdown, [1, 2, 0]);
        AssertBreakdownRendersInReverseOrderWithCounts(spikeBreakdown, [1, 2, 0]);
        AssertBreakdownParenthesized(plainBreakdown, [1, 2, 0]);
        AssertBreakdownParenthesized(spikeBreakdown, [1, 2, 0]);
        Assert.DoesNotContain("Alpha", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha", spike, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }

    [Fact]
    public void RegionAria_ErrorCodeEventsSingleCount_RendersSingularErrorCodeEvent()
    {
        string text = HistogramTextComposer.RegionAria(_localizer, total: 1, HistogramEventNoun.ErrorCodeEvents, Start(), End(), []);

        AssertContainsGeneralShortWindow(text);
        AssertEventNounInflects(HistogramEventNoun.ErrorCodeEvents);
        AssertCountNounAdjacent(text, 1, HistogramEventNoun.ErrorCodeEvents);
    }

    [Fact]
    public void RegionAria_FormatsDateTimeWithGeneralShortPatternAndSingularOneCount()
    {
        string text = HistogramTextComposer.RegionAria(
            _localizer,
            total: 1,
            HistogramEventNoun.Events,
            Start(),
            End(),
            []);

        AssertContainsGeneralShortWindow(text);
        AssertEventNounInflects(HistogramEventNoun.Events);
        AssertCountNounAdjacent(text, 1, HistogramEventNoun.Events);
    }

    [Fact]
    public void RegionAria_WithBreakdown_AppendsCommaSeparatedReverseOrderedItems()
    {
        IReadOnlyList<HistogramBreakdownItem> items =
            HistogramTextComposer.GroupBreakdownItems([1, 2, 7], Groups(), groupHighlightText: null);

        string text = HistogramTextComposer.RegionAria(_localizer, total: 1200, HistogramEventNoun.Events, Start(), End(), items);

        AssertContainsGeneralShortWindow(text);
        AssertContainsRawCount(text, 1200);
        AssertCountNounAdjacent(text, 1200, HistogramEventNoun.Events);
        AssertBreakdownRendersInReverseOrderWithCounts(text, [1, 2, 7]);
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

        AssertContainsGeneralShortWindow(text);
        AssertContainsRawCount(text, 1200);
        AssertCountNounAdjacent(text, 1200, HistogramEventNoun.Events);
    }

    [Fact]
    public void WindowAnnouncement_WithBreakdown_AppendsCommaSeparatedReverseOrderedItems()
    {
        IReadOnlyList<HistogramBreakdownItem> items =
            HistogramTextComposer.GroupBreakdownItems([1, 2, 7], Groups(), groupHighlightText: null);

        string text = HistogramTextComposer.WindowAnnouncement(_localizer, total: 1200, HistogramEventNoun.Events, Start(), End(), items);

        AssertContainsGeneralShortWindow(text);
        AssertContainsRawCount(text, 1200);
        AssertCountNounAdjacent(text, 1200, HistogramEventNoun.Events);
        AssertBreakdownRendersInReverseOrderWithCounts(text, [1, 2, 7]);
    }

    private static void AssertContainsGeneralShortWindow(string text)
    {
        Assert.Contains(Start().ToString("g", CultureInfo.CurrentCulture), text, StringComparison.Ordinal);
        Assert.Contains(End().ToString("g", CultureInfo.CurrentCulture), text, StringComparison.Ordinal);
    }

    private static void AssertContainsRawCount(string text, int count)
    {
        Assert.Contains(count.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal);

        if (count >= 1000)
        {
            Assert.DoesNotContain(count.ToString("N0", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        }
    }

    private static int CommonPrefixLength(string first, string second)
    {
        int max = Math.Min(first.Length, second.Length);
        int index = 0;

        while (index < max && first[index] == second[index]) { index++; }

        return index;
    }

    private static int CommonSuffixLength(string first, string second)
    {
        int max = Math.Min(first.Length, second.Length);
        int index = 0;

        while (index < max && first[first.Length - 1 - index] == second[second.Length - 1 - index]) { index++; }

        return index;
    }

    private static DateTime End() => new(2024, 1, 1, 14, 0, 30, DateTimeKind.Unspecified);

    private static IReadOnlyList<HistogramGroup> Groups() =>
    [
        new(new HistogramGroupLabel.DataValue("Alpha"), "a", "a", [0]),
        new(new HistogramGroupLabel.CategoricalOther(HistogramDimension.Source, 1), "other", "other", [1]),
        new(new HistogramGroupLabel.DataValue("Bravo"), "b", "b", [2])
    ];

    private static string NormalizeFormattedNumbers(string value) =>
        Regex.Replace(value, @"\d+(?:,\d{3})*(?:\.\d+)?", "#");

    private static DateTime Start() => new(2024, 1, 1, 13, 45, 30, DateTimeKind.Unspecified);

    // The rendered breakdown items must sit inside a single "(...)" pair (the parenthesized templates).
    private void AssertBreakdownParenthesized(string text, int[] totals)
    {
        IReadOnlyList<string> fragments = ExpectedBreakdownFragments(totals);

        Assert.NotEmpty(fragments);
        Assert.Contains("(" + fragments[0], text, StringComparison.Ordinal);
        Assert.Contains(fragments[^1] + ")", text, StringComparison.Ordinal);
    }

    // Reverse-group order is asserted against an INDEPENDENT oracle: this walks the group fixture from last index to
    // first (zero-count groups omitted) rather than calling the production GroupBreakdownItems, so a regression from
    // reverse to forward order fails here instead of moving the expected and actual together. Only each label is
    // rendered through the production formatter. Consecutive items must also be joined by the localized separator.
    private void AssertBreakdownRendersInReverseOrderWithCounts(string text, int[] totals)
    {
        string separator = _localizer["Histogram_Breakdown_Separator"].Value;
        IReadOnlyList<string> fragments = ExpectedBreakdownFragments(totals);

        Assert.NotEmpty(fragments);

        int previousEnd = -1;

        foreach (string fragment in fragments)
        {
            int index = text.IndexOf(fragment, StringComparison.Ordinal);

            Assert.True(index > previousEnd, $"Expected '{fragment}' after the previous breakdown item in '{text}'.");

            if (previousEnd >= 0)
            {
                Assert.Contains(separator, text[previousEnd..index], StringComparison.Ordinal);
            }

            previousEnd = index + fragment.Length;
        }
    }

    // The count must render immediately adjacent to the correctly inflected noun for this count (computed from the same
    // composer, so it survives copy changes), which a bare "the count digit appears somewhere" check cannot prove.
    private void AssertCountNounAdjacent(string text, int count, HistogramEventNoun noun)
    {
        string renderedNoun = HistogramTextComposer.EventNoun(_localizer, noun, count);
        string fragment = string.Create(CultureInfo.InvariantCulture, $"{count} {renderedNoun}");

        Assert.Contains(fragment, text, StringComparison.Ordinal);
    }

    private void AssertEventNounInflects(HistogramEventNoun noun) =>
        Assert.NotEqual(
            NormalizeFormattedNumbers(HistogramTextComposer.EventNoun(_localizer, noun, 1)),
            NormalizeFormattedNumbers(HistogramTextComposer.EventNoun(_localizer, noun, 2)));

    private string BinCursor(bool isSpike, IReadOnlyList<HistogramBreakdownItem> breakdownItems) =>
        HistogramTextComposer.BinCursorAnnouncement(
            _localizer,
            total: 1,
            HistogramEventNoun.Events,
            Start(),
            End(),
            isSpike,
            breakdownItems);

    // Independent expectation of the rendered breakdown fragments ("<count> <label>") in reverse group order, zero-count
    // groups omitted. Does not call GroupBreakdownItems, so it can serve as the oracle for the production ordering.
    private IReadOnlyList<string> ExpectedBreakdownFragments(int[] totals)
    {
        IReadOnlyList<HistogramGroup> groups = Groups();
        var fragments = new List<string>();

        for (int group = groups.Count - 1; group >= 0; group--)
        {
            if (totals[group] <= 0) { continue; }

            string label = HistogramGroupLabelFormatter.Format(_localizer, groups[group].Label);
            fragments.Add(string.Create(CultureInfo.InvariantCulture, $"{totals[group]} {label}"));
        }

        return fragments;
    }

    // The spike marker, derived from the resource templates rather than hardcoded, is whatever the spike template adds
    // to the plain template (the middle span between their shared prefix and shared suffix).
    private string SpikeMarkerFromTemplates()
    {
        string plainTemplate = _localizer["Histogram_BinCursor"].Value;
        string spikeTemplate = _localizer["Histogram_BinCursor_Spike"].Value;
        int prefix = CommonPrefixLength(plainTemplate, spikeTemplate);
        int suffix = CommonSuffixLength(plainTemplate, spikeTemplate);
        string marker = spikeTemplate[prefix..(spikeTemplate.Length - suffix)];

        Assert.False(string.IsNullOrEmpty(marker), "The spike template must add a marker segment over the plain template.");

        return marker;
    }
}

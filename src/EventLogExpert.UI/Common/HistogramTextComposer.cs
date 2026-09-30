// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Histogram;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Common;

internal static class HistogramTextComposer
{
    internal static string BarTooltip(
        IStringLocalizer<SharedResource> localizer,
        int total,
        HistogramEventNoun eventNoun,
        DateTime start,
        DateTime end,
        bool windowCrossesDay,
        IReadOnlyList<HistogramBreakdownItem> breakdownItems)
    {
        // Timeline labels use fixed Gregorian text for stable axis alignment.
        string startText = windowCrossesDay ?
            string.Create(CultureInfo.InvariantCulture, $"{start:yyyy-MM-dd HH\\:mm\\:ss}") :
            string.Create(CultureInfo.InvariantCulture, $"{start:HH\\:mm\\:ss}");
        string endText = windowCrossesDay ?
            string.Create(CultureInfo.InvariantCulture, $"{end:yyyy-MM-dd HH\\:mm\\:ss}") :
            string.Create(CultureInfo.InvariantCulture, $"{end:HH\\:mm\\:ss}");
        string noun = EventNoun(localizer, eventNoun, total);
        string breakdown = GroupBreakdown(localizer, breakdownItems);

        return string.IsNullOrEmpty(breakdown) ?
            localizer["Histogram_BarTooltip", total, noun, startText, endText] :
            localizer["Histogram_BarTooltip_Breakdown", total, noun, startText, endText, breakdown];
    }

    internal static string BinCursorAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        int total,
        HistogramEventNoun eventNoun,
        DateTime start,
        DateTime end,
        bool isSpike,
        IReadOnlyList<HistogramBreakdownItem> breakdownItems)
    {
        string noun = EventNoun(localizer, eventNoun, total);
        string breakdown = GroupBreakdown(localizer, breakdownItems);

        return (isSpike, string.IsNullOrEmpty(breakdown)) switch
        {
            (false, true) => localizer["Histogram_BinCursor", start, end, total, noun],
            (true, true) => localizer["Histogram_BinCursor_Spike", start, end, total, noun],
            (false, false) => localizer["Histogram_BinCursor_Breakdown", start, end, total, noun, breakdown],
            _ => localizer["Histogram_BinCursor_Spike_Breakdown", start, end, total, noun, breakdown]
        };
    }

    internal static string EventNoun(
        IStringLocalizer<SharedResource> localizer,
        HistogramEventNoun eventNoun,
        int count) => eventNoun switch
        {
            HistogramEventNoun.Events => PluralText.Format(localizer, "Histogram_EventNoun_Events", ("count", count)),
            HistogramEventNoun.ErrorCodeEvents => PluralText.Format(localizer, "Histogram_EventNoun_ErrorCodeEvents", ("count", count)),
            _ => throw new ArgumentOutOfRangeException(nameof(eventNoun), eventNoun, null)
        };

    internal static string GroupBreakdown(
        IStringLocalizer<SharedResource> localizer,
        IReadOnlyList<HistogramBreakdownItem> items)
    {
        var parts = new List<string>();

        foreach (HistogramBreakdownItem item in items)
        {
            if (item.Count <= 0) { continue; }

            string label = HistogramGroupLabelFormatter.Format(localizer, item.Label);

            parts.Add(string.IsNullOrEmpty(item.HighlightText) ?
                localizer["Histogram_BreakdownItem", item.Count, label] :
                localizer["Histogram_BreakdownItem_Highlighted", item.Count, label, item.HighlightText]);
        }

        return string.Join(localizer["Histogram_Breakdown_Separator"].Value, parts);
    }

    internal static IReadOnlyList<HistogramBreakdownItem> GroupBreakdownItems(
        int[] totals,
        IReadOnlyList<HistogramGroup> groups,
        Func<int, string>? groupHighlightText)
    {
        var items = new List<HistogramBreakdownItem>();

        for (int group = groups.Count - 1; group >= 0; group--)
        {
            if (totals[group] <= 0) { continue; }

            string highlightText = groupHighlightText?.Invoke(group) ?? string.Empty;
            items.Add(new HistogramBreakdownItem(totals[group], groups[group].Label, highlightText));
        }

        return items;
    }

    internal static string RegionAria(
        IStringLocalizer<SharedResource> localizer,
        int total,
        HistogramEventNoun eventNoun,
        DateTime start,
        DateTime end,
        IReadOnlyList<HistogramBreakdownItem> breakdownItems)
    {
        string noun = EventNoun(localizer, eventNoun, total);
        string breakdown = GroupBreakdown(localizer, breakdownItems);

        return string.IsNullOrEmpty(breakdown) ?
            localizer["Histogram_RegionAria", total, noun, start, end] :
            localizer["Histogram_RegionAria_Breakdown", total, noun, start, end, breakdown];
    }

    internal static string WindowAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        int total,
        HistogramEventNoun eventNoun,
        DateTime start,
        DateTime end,
        IReadOnlyList<HistogramBreakdownItem> breakdownItems)
    {
        string noun = EventNoun(localizer, eventNoun, total);
        string breakdown = GroupBreakdown(localizer, breakdownItems);

        return string.IsNullOrEmpty(breakdown) ?
            localizer["Histogram_WindowAnnouncement", total, noun, start, end] :
            localizer["Histogram_WindowAnnouncement_Breakdown", total, noun, start, end, breakdown];
    }
}

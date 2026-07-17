// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public static class HistogramSummary
{
    public static string RegionLabel(HistogramData data, TimeZoneInfo displayZone)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(displayZone);

        var min = TimeZoneInfo.ConvertTimeFromUtc(data.MinUtc, displayZone);
        var max = TimeZoneInfo.ConvertTimeFromUtc(data.MaxUtc, displayZone);

        return $"Timeline: {data.Total} {data.EventNoun} from {min:g} to {max:g}{GroupBreakdown(GroupTotals(data), data.Groups)}.";
    }

    public static string WindowAnnouncement(HistogramRender render, IReadOnlyList<HistogramGroup> groups, string eventNoun, TimeZoneInfo displayZone)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentException.ThrowIfNullOrEmpty(eventNoun);
        ArgumentNullException.ThrowIfNull(displayZone);

        var start = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(render.WindowStartTicks, DateTimeKind.Utc), displayZone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(render.WindowEndTicks, DateTimeKind.Utc), displayZone);

        return $"Showing {start:g} to {end:g}: {render.WindowTotal} {eventNoun}{GroupBreakdown(render.WindowGroupTotals, groups)}.";
    }

    private static string GroupBreakdown(int[] totals, IReadOnlyList<HistogramGroup> groups)
    {
        var parts = new List<string>();

        for (int group = groups.Count - 1; group >= 0; group--)
        {
            if (totals[group] > 0) { parts.Add($"{totals[group]} {groups[group].Label}"); }
        }

        return parts.Count == 0 ? string.Empty : ", " + string.Join(", ", parts);
    }

    private static int[] GroupTotals(HistogramData data)
    {
        var totals = new int[data.Groups.Count];

        for (int bin = 0; bin < data.BinCount; bin++)
        {
            int offset = bin * data.SlotCount;

            for (int group = 0; group < data.Groups.Count; group++)
            {
                foreach (int slot in data.Groups[group].SlotIndices) { totals[group] += data.SlotCounts[offset + slot]; }
            }
        }

        return totals;
    }
}

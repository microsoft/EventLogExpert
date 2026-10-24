// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Display;
using System.Globalization;

namespace EventLogExpert.Runtime.FilterLenses;

public static class FilterLensLabelText
{
    /// <summary>
    ///     Formats a time-window radius as a compact technical duration (<c>h</c>/<c>m</c>/<c>s</c>). The unit glyphs are
    ///     invariant (like ISO durations) in both the invariant text and the localized chip, so this helper is shared.
    /// </summary>
    public static string FormatRadius(TimeSpan radius) => radius switch
    {
        { Minutes: 0, Seconds: 0, Milliseconds: 0 } => $"{radius.TotalHours:0}h",
        { Seconds: 0, Milliseconds: 0 } => $"{radius.TotalMinutes:0}m",
        _ => $"{radius.TotalSeconds:0}s"
    };

    internal static string Invariant(FilterLensLabel label, TimeZoneInfo displayZone) =>
        label switch
        {
            FilterLensLabel.PropertyComparison(var property, var isEqual, var value) =>
                $"{property.ToFullString()} {(isEqual ? "=" : "\u2260")} {value}",
            FilterLensLabel.ParentActivity(var activityId) => $"Parent Activity = {activityId}",
            FilterLensLabel.TimeRange(var afterUtc, var beforeUtc) => InvariantTimeRange(
                afterUtc.ConvertTimeZone(displayZone),
                beforeUtc.ConvertTimeZone(displayZone)),
            FilterLensLabel.TimeWindow(var centerUtc, var radius) => string.Format(CultureInfo.InvariantCulture,
                "Near {0:T} \u00b1{1}",
                centerUtc.ConvertTimeZone(displayZone),
                FormatRadius(radius)),
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, null)
        };

    private static string InvariantTimeRange(DateTime afterLocal, DateTime beforeLocal) =>
        afterLocal.Date == beforeLocal.Date ?
            string.Format(CultureInfo.InvariantCulture, "{0:T} - {1:T}", afterLocal, beforeLocal) :
            string.Format(CultureInfo.InvariantCulture, "{0:d} {0:T} - {1:d} {1:T}", afterLocal, beforeLocal);
}

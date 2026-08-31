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

    public static string Invariant(FilterLensLabel label) =>
        label switch
        {
            FilterLensLabel.PropertyComparison(var property, var isEqual, var value) =>
                $"{property.ToFullString()} {(isEqual ? "=" : "\u2260")} {value}",
            FilterLensLabel.ParentActivity(var activityId) => $"Parent Activity = {activityId}",
            FilterLensLabel.TimeRange(var afterLocal, var beforeLocal, var sameDay) => sameDay ?
                string.Format(CultureInfo.InvariantCulture, "{0:T} - {1:T}", afterLocal, beforeLocal) :
                string.Format(CultureInfo.InvariantCulture, "{0:d} {0:T} - {1:d} {1:T}", afterLocal, beforeLocal),
            FilterLensLabel.TimeWindow(var centerLocal, var radius) => string.Format(CultureInfo.InvariantCulture,
                "Near {0:T} \u00b1{1}",
                centerLocal,
                FormatRadius(radius)),
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, null)
        };
}

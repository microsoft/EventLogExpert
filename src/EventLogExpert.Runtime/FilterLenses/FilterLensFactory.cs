// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Common.Display;

namespace EventLogExpert.Runtime.FilterLenses;

internal static class FilterLensFactory
{
    private static readonly TimeSpan s_maxTimeWindowRadius = TimeSpan.FromHours(1);

    public static FilterLens? ForActivityId(Guid activityId, string? originLog = null, string? label = null) =>
        BuildEqualityLens(EventProperty.ActivityId, activityId, label ?? $"Activity ID = {activityId}", originLog);

    public static FilterLens? ForRelatedActivityId(Guid relatedActivityId, string? originLog = null) =>
        BuildEqualityLens(
            EventProperty.RelatedActivityId,
            relatedActivityId,
            $"Related Activity ID = {relatedActivityId}",
            originLog);

    public static FilterLens ForTimeRange(
        DateTime startUtc,
        DateTime endUtc,
        TimeZoneInfo displayZone,
        string? originLog = null)
    {
        var (after, before) = startUtc <= endUtc ? (startUtc, endUtc) : (endUtc, startUtc);
        var afterLocal = after.ConvertTimeZone(displayZone);
        var beforeLocal = before.ConvertTimeZone(displayZone);

        return new FilterLens
        {
            Label = afterLocal.Date == beforeLocal.Date
                ? $"{afterLocal:T} - {beforeLocal:T}"
                : $"{afterLocal:d} {afterLocal:T} - {beforeLocal:d} {beforeLocal:T}",
            Kind = LensKind.TimeWindow,
            Window = new DateFilter { After = after, Before = before, IsEnabled = true },
            OriginLog = originLog
        };
    }

    public static FilterLens ForTimeWindow(
        DateTime timeCreatedUtc,
        TimeSpan radius,
        TimeZoneInfo displayZone,
        string? originLog = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(radius, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, s_maxTimeWindowRadius);

        if (radius.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be a whole number of seconds.");
        }

        var after = timeCreatedUtc <= DateTime.MinValue + radius ? DateTime.MinValue : timeCreatedUtc - radius;
        var before = timeCreatedUtc >= DateTime.MaxValue - radius ? DateTime.MaxValue : timeCreatedUtc + radius;

        return new FilterLens
        {
            Label = $"Near {timeCreatedUtc.ConvertTimeZone(displayZone):T} \u00b1{FormatRadius(radius)}",
            Kind = LensKind.TimeWindow,
            Window = new DateFilter { After = after, Before = before, IsEnabled = true },
            OriginLog = originLog
        };
    }

    private static FilterLens? BuildEqualityLens(EventProperty property, Guid value, string label, string? originLog)
    {
        if (!TryFormatNotEqual(property, value.ToString(), out var comparisonText))
        {
            return null;
        }

        var complement = SavedFilter.TryCreate(
            comparisonText,
            isExcluded: true,
            isEnabled: true,
            mode: FilterMode.Advanced);

        if (complement?.Compiled is null) { return null; }

        return new FilterLens
        {
            Label = label,
            Kind = LensKind.Property,
            ExcludeFilters = [complement],
            OriginLog = originLog
        };
    }

    private static string FormatRadius(TimeSpan radius) => radius switch
    {
        { Minutes: 0, Seconds: 0, Milliseconds: 0 } => $"{radius.TotalHours:0}h",
        { Seconds: 0, Milliseconds: 0 } => $"{radius.TotalMinutes:0}m",
        _ => $"{radius.TotalSeconds:0}s"
    };

    private static bool TryFormatNotEqual(EventProperty property, string value, out string comparisonText)
    {
        var comparison = new FilterComparison
        {
            Property = property,
            Operator = ComparisonOperator.NotEqual,
            MatchMode = MatchMode.Single,
            Value = value
        };

        return BasicFilterFormatter.TryFormat(new BasicFilter(comparison, []), out comparisonText);
    }
}

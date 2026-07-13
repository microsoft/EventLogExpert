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
    /// <summary>
    ///     Builds a lens that keeps only rows whose ActivityId equals <paramref name="activityId" />. An optional
    ///     <paramref name="label" /> overrides the default chip text - used by the parent-activity jump, which is an
    ///     ActivityId-equality narrowing surfaced to the user under a different name.
    /// </summary>
    public static FilterLens? ForActivityId(Guid activityId, string? originLog = null, string? label = null) =>
        BuildEqualityLens(EventProperty.ActivityId, activityId, label ?? $"Activity ID = {activityId}", originLog);

    /// <summary>
    ///     Builds a lens that keeps only rows whose RelatedActivityId equals <paramref name="relatedActivityId" />,
    ///     grouping events that share the same parent/correlation activity.
    /// </summary>
    public static FilterLens? ForRelatedActivityId(Guid relatedActivityId, string? originLog = null) =>
        BuildEqualityLens(
            EventProperty.RelatedActivityId,
            relatedActivityId,
            $"Related Activity ID = {relatedActivityId}",
            originLog);

    /// <summary>
    ///     Builds a transient time-window lens centered on <paramref name="timeCreatedUtc" /> (the source event's UTC
    ///     timestamp): the effective view is narrowed to the inclusive range [timeCreatedUtc - radius, timeCreatedUtc +
    ///     radius], so the source event itself always survives. Bounds are clamped to the <see cref="DateTime" /> range
    ///     because boundary arithmetic throws on overflow - a degenerate near-min/near-max timestamp must not crash the menu
    ///     handler. The chip label renders the anchor's time of day in <paramref name="displayZone" /> (the grid's display
    ///     zone) as a compact marker; it is not the grid's full date+time rendering. <paramref name="radius" /> must be within
    ///     (0, 1 hour] and a whole number of seconds (validated); the suffix renders it to its largest whole unit (for example
    ///     90s stays "90s").
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="radius" /> is not within (0, 1 hour], or is not a whole
    ///     number of seconds.
    /// </exception>
    public static FilterLens ForTimeWindow(
        DateTime timeCreatedUtc,
        TimeSpan radius,
        TimeZoneInfo displayZone,
        string? originLog = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(radius, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, TimeSpan.FromHours(1));

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

    /// <summary>
    ///     Encodes a nullable-Guid equality narrowing (keep only rows where the field equals <paramref name="value" />)
    ///     as an <em>exclude-of-complement</em>: because the base's include filters are OR-combined an appended include would
    ///     broaden, so the complement (<c>field != value</c>, <see cref="SavedFilter.IsExcluded" />) is excluded to AND-narrow
    ///     to exactly <c>field == value</c>. <c>NotEqual</c> on a nullable-Guid column is total (a decisive Match for an
    ///     absent value), so the exclude hides absent-field rows rather than leaking them. Returns <see langword="null" />
    ///     only if the criterion fails to format or compile.
    /// </summary>
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

    /// <summary>
    ///     Formats the window radius as a compact chip suffix (for example "30s", "5m", "1h") using the largest whole
    ///     unit that represents it exactly. Callers are validated to whole-second radii, so every valid radius renders
    ///     losslessly.
    /// </summary>
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

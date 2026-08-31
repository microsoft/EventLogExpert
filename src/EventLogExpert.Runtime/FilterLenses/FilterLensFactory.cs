// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Common.Display;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLenses;

internal static class FilterLensFactory
{
    private static readonly TimeSpan s_maxTimeWindowRadius = TimeSpan.FromHours(1);

    public static FilterLens? ForActivityId(Guid activityId, string? originLog = null) =>
        BuildEqualityLens(
            EventProperty.ActivityId,
            activityId,
            new FilterLensLabel.PropertyComparison(EventProperty.ActivityId, IsEqual: true, activityId.ToString()),
            originLog);

    public static FilterLens? ForExcludedValue(EventProperty property, string value, string? originLog = null)
    {
        // Hide every event whose property equals this value: an EXCLUDED Equal comparison (the mirror of the keep-only
        // NotEqual comparison BuildEqualityLens uses). The resulting view keeps events where property != value.
        if (!TryFormatEqual(property, value, out var comparisonText)) { return null; }

        var excluded = SavedFilter.TryCreate(
            comparisonText,
            isExcluded: true,
            isEnabled: true,
            mode: FilterMode.Advanced);

        if (excluded?.Compiled is null) { return null; }

        return new FilterLens
        {
            Label = new FilterLensLabel.PropertyComparison(property, IsEqual: false, value),
            Kind = LensKind.Property,
            ExcludeFilters = [excluded],
            OriginLog = originLog
        };
    }

    public static FilterLens? ForIncludedValue(EventProperty property, string value, string? originLog = null)
    {
        // Keep only events whose property equals this value by EXCLUDING everything that does not match: an excluded
        // NotEqual comparison (the same keep-only mechanism BuildEqualityLens uses for activity ids).
        if (!TryFormatNotEqual(property, value, out var comparisonText)) { return null; }

        var complement = SavedFilter.TryCreate(
            comparisonText,
            isExcluded: true,
            isEnabled: true,
            mode: FilterMode.Advanced);

        if (complement?.Compiled is null) { return null; }

        // The merged User field is presence-gated: `User != value` is NoMatch (i.e. kept) for events with no user
        // identity, so the NotEqual complement alone would leak every no-user event into this keep-only view. Drop
        // those rows with a second exclude clause (`User == null` matches exactly the no-identity events) so the kept
        // set is precisely `User == value`. Source/Event ID/Task Category negate totally, so their lone complement
        // already excludes absent values and needs no second clause.
        SavedFilter? absentComplement = null;

        if (property == EventProperty.UserDisplayName)
        {
            absentComplement = SavedFilter.TryCreate(
                "User == null",
                isExcluded: true,
                isEnabled: true,
                mode: FilterMode.Advanced);

            if (absentComplement?.Compiled is null) { return null; }
        }

        return new FilterLens
        {
            Label = new FilterLensLabel.PropertyComparison(property, IsEqual: true, value),
            Kind = LensKind.Property,
            ExcludeFilters = absentComplement is null ? [complement] : [complement, absentComplement],
            PromoteFilters = BuildKeepOnlyPromoteForm(property, value),
            OriginLog = originLog
        };
    }

    public static FilterLens? ForParentActivity(Guid activityId, string? originLog = null) =>
        BuildEqualityLens(
            EventProperty.ActivityId,
            activityId,
            new FilterLensLabel.ParentActivity(activityId),
            originLog);

    public static FilterLens? ForRelatedActivityId(Guid relatedActivityId, string? originLog = null) =>
        BuildEqualityLens(
            EventProperty.RelatedActivityId,
            relatedActivityId,
            new FilterLensLabel.PropertyComparison(EventProperty.RelatedActivityId, IsEqual: true, relatedActivityId.ToString()),
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
            Label = new FilterLensLabel.TimeRange(afterLocal, beforeLocal, afterLocal.Date == beforeLocal.Date),
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
            Label = new FilterLensLabel.TimeWindow(timeCreatedUtc.ConvertTimeZone(displayZone), radius),
            Kind = LensKind.TimeWindow,
            Window = new DateFilter { After = after, Before = before, IsEnabled = true },
            OriginLog = originLog
        };
    }

    private static FilterLens? BuildEqualityLens(EventProperty property, Guid value, FilterLensLabel label, string? originLog)
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
            PromoteFilters = BuildKeepOnlyPromoteForm(property, value.ToString()),
            OriginLog = originLog
        };
    }

    private static ImmutableList<SavedFilter> BuildKeepOnlyPromoteForm(EventProperty property, string value)
    {
        if (!TryFormatEqual(property, value, out var comparisonText)) { return []; }

        var include = SavedFilter.TryCreate(comparisonText, isExcluded: false, isEnabled: true, mode: FilterMode.Advanced);

        return include?.Compiled is null ? [] : [include];
    }

    private static bool TryFormatEqual(EventProperty property, string value, out string comparisonText)
    {
        var comparison = new FilterComparison
        {
            Property = property,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = value
        };

        return BasicFilterFormatter.TryFormat(new BasicFilter(comparison, []), out comparisonText);
    }

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

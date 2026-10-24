// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;

namespace EventLogExpert.Runtime.FilterLenses;

/// <summary>
///     The single source of truth for building the effective applied filter from a persistent base filter plus the
///     active transient lens stack. It is the ONLY place lens composition happens - both the FilterLens push/pop effect
///     and the FilterPane apply path call it, so the two dispatch sites cannot diverge. A property lens contributes
///     exclude criteria that are appended (AND-narrowing); a time-window lens intersects the date filter. The base filter
///     is never mutated.
/// </summary>
internal static class EffectiveFilterBuilder
{
    public static Filter Build(Filter baseFilter, IReadOnlyList<FilterLens> lenses)
    {
        if (lenses.Count == 0) { return baseFilter; }

        var filters = baseFilter.Filters;
        DateFilter? date = baseFilter.DateFilter;

        foreach (var lens in lenses)
        {
            if (!lens.ExcludeFilters.IsEmpty)
            {
                filters = filters.AddRange(lens.ExcludeFilters);
            }

            if (lens.Window is { IsEnabled: true } window)
            {
                date = IntersectWindow(date, window);
            }
        }

        return new Filter(date, filters);
    }

    /// <summary>
    ///     Intersects <paramref name="window" /> with <paramref name="baseDate" /> (null or disabled is unbounded) into an
    ///     enabled filter where a null bound stays open.
    /// </summary>
    internal static DateFilter IntersectWindow(DateFilter? baseDate, DateFilter window)
    {
        var baseActive = baseDate is { IsEnabled: true };

        var after = Later(
            baseActive ? baseDate!.After ?? DateTime.MinValue : DateTime.MinValue,
            window.After ?? DateTime.MinValue);

        var before = Earlier(
            baseActive ? baseDate!.Before ?? DateTime.MaxValue : DateTime.MaxValue,
            window.Before ?? DateTime.MaxValue);

        return new DateFilter
        {
            After = after == DateTime.MinValue ? null : after,
            Before = before == DateTime.MaxValue ? null : before,
            IsEnabled = true
        };
    }

    private static DateTime Earlier(DateTime first, DateTime second) => first <= second ? first : second;

    private static DateTime Later(DateTime first, DateTime second) => first >= second ? first : second;
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Models;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.UI.Services;

/// <summary>
/// Caches the distinct, sorted list of category values across all active logs, keyed by the
/// <see cref="ImmutableDictionary{TKey, TValue}"/> snapshot reference. Each snapshot change
/// produces a new key, so cache entries auto-evict via <see cref="ConditionalWeakTable{TKey, TValue}"/>.
/// </summary>
public static class FilterCategoryItemsCache
{
    private static readonly ConditionalWeakTable<
        ImmutableDictionary<string, EventLogData>,
        ConcurrentDictionary<FilterCategory, Lazy<ImmutableArray<string>>>> s_cache = new();
    private static readonly ImmutableArray<string> s_levelItems = [.. Enum.GetNames<SeverityLevel>()];

    /// <summary>Test-only hook to clear the snapshot cache between scenarios.</summary>
    public static void Clear() => s_cache.Clear();

    public static ImmutableArray<string> GetItems(
        ImmutableDictionary<string, EventLogData> activeLogs,
        FilterCategory category)
    {
        if (category is FilterCategory.Level)
        {
            return s_levelItems;
        }

        if (!IsLogDerivedCategory(category))
        {
            return [];
        }

        var perSnapshot = s_cache.GetValue(
            activeLogs,
            static _ => new ConcurrentDictionary<FilterCategory, Lazy<ImmutableArray<string>>>());

        return perSnapshot
            .GetOrAdd(category, c => new Lazy<ImmutableArray<string>>(() => Compute(activeLogs, c)))
            .Value;
    }

    private static ImmutableArray<string> Compute(
        ImmutableDictionary<string, EventLogData> activeLogs,
        FilterCategory category) =>
        [.. activeLogs.Values.SelectMany(log => log.GetCategoryValues(category)).Distinct().Order()];

    private static bool IsLogDerivedCategory(FilterCategory category) => category is
        FilterCategory.Id or
        FilterCategory.ActivityId or
        FilterCategory.Keywords or
        FilterCategory.Source or
        FilterCategory.TaskCategory;
}

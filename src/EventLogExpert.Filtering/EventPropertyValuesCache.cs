// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Filtering;

/// <summary>
///     Caches the distinct, sorted list of property values across all active logs, keyed by the
///     <see cref="ImmutableDictionary{TKey,TValue}" /> snapshot reference. Each snapshot change produces a new key, so
///     cache entries auto-evict via <see cref="ConditionalWeakTable{TKey,TValue}" />.
/// </summary>
public static class EventPropertyValuesCache
{
    private static readonly ConditionalWeakTable<
        ImmutableDictionary<string, EventLogData>,
        ConcurrentDictionary<EventProperty, Lazy<ImmutableArray<string>>>> s_cache = new();
    private static readonly ImmutableArray<string> s_levelValues = [.. Enum.GetNames<SeverityLevel>()];

    public static ImmutableArray<string> GetValues(
        ImmutableDictionary<string, EventLogData> activeLogs,
        EventProperty property)
    {
        if (property is EventProperty.Level)
        {
            return s_levelValues;
        }

        if (!IsLogDerivedProperty(property))
        {
            return [];
        }

        var perSnapshot = s_cache.GetValue(
            activeLogs,
            static _ => new ConcurrentDictionary<EventProperty, Lazy<ImmutableArray<string>>>());

        return perSnapshot
            .GetOrAdd(property, f => new Lazy<ImmutableArray<string>>(() => Compute(activeLogs, f)))
            .Value;
    }

    /// <summary>Test-only hook to clear the snapshot cache between scenarios.</summary>
    internal static void Clear() => s_cache.Clear();

    private static ImmutableArray<string> Compute(
        ImmutableDictionary<string, EventLogData> activeLogs,
        EventProperty property) =>
        [.. activeLogs.Values.SelectMany(log => log.GetEventValues(property)).Distinct().Order()];

    private static bool IsLogDerivedProperty(EventProperty property) => property is
        EventProperty.Id or
        EventProperty.ActivityId or
        EventProperty.Keywords or
        EventProperty.Source or
        EventProperty.TaskCategory;
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Filtering.EventData;

/// <summary>
///     Caches the distinct, sorted list of property values across a set of events, keyed by an opaque snapshot
///     reference the caller supplies (stable per event snapshot). Each snapshot change produces a new key, so cache
///     entries auto-evict via <see cref="ConditionalWeakTable{TKey,TValue}" />.
/// </summary>
public static class EventPropertyValuesCache
{
    private static readonly ConditionalWeakTable<
        object,
        ConcurrentDictionary<EventProperty, Lazy<ImmutableArray<string>>>> s_cache = new();
    private static readonly ImmutableArray<string> s_levelValues = [.. Enum.GetNames<SeverityLevel>()];

    public static ImmutableArray<string> GetValues(
        object cacheKey,
        IEnumerable<ResolvedEvent> events,
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
            cacheKey,
            static _ => new ConcurrentDictionary<EventProperty, Lazy<ImmutableArray<string>>>());

        return perSnapshot
            .GetOrAdd(property, f => new Lazy<ImmutableArray<string>>(() => Compute(events, f)))
            .Value;
    }

    /// <summary>Test-only hook to clear the snapshot cache between scenarios.</summary>
    internal static void Clear() => s_cache.Clear();

    private static ImmutableArray<string> Compute(
        IEnumerable<ResolvedEvent> events,
        EventProperty property) =>
        [.. events.GetEventValues(property).Distinct().Order()];

    private static bool IsLogDerivedProperty(EventProperty property) => property is
        EventProperty.Id or
        EventProperty.ActivityId or
        EventProperty.Keywords or
        EventProperty.Source or
        EventProperty.TaskCategory or
        EventProperty.LogName;
}

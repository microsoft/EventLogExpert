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
    private static readonly ConditionalWeakTable<object, Lazy<ImmutableArray<string>>> s_fieldNameCache = new();
    private static readonly ConditionalWeakTable<
        object,
        ConcurrentDictionary<string, Lazy<ImmutableArray<string>>>> s_fieldValueCache = new();
    private static readonly ImmutableArray<string> s_levelValues = [.. Enum.GetNames<SeverityLevel>()];

    /// <summary>
    ///     Returns the distinct, sorted &lt;EventData&gt; field names across <paramref name="events" />, cached per
    ///     snapshot <paramref name="cacheKey" /> (auto-evicted when the snapshot changes).
    /// </summary>
    public static ImmutableArray<string> GetEventDataFieldNames(object cacheKey, IEnumerable<ResolvedEvent> events) =>
        s_fieldNameCache
            .GetValue(
                cacheKey,
                _ => new Lazy<ImmutableArray<string>>(
                    () => [.. events.GetEventDataFieldNames().Order(StringComparer.Ordinal)]))
            .Value;

    /// <summary>
    ///     Returns the distinct, sorted values of the named EventData <paramref name="fieldName" /> across
    ///     <paramref name="events" />, cached per <c>(snapshot, fieldName)</c> so each field keeps its own value list.
    /// </summary>
    public static ImmutableArray<string> GetEventDataFieldValues(
        object cacheKey,
        IEnumerable<ResolvedEvent> events,
        string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return [];
        }

        // Only enumerate/cache values for field names that actually exist in the snapshot. The Basic editor's
        // field-name picker is editable, so without this an arbitrary or partial typed name would add a cache entry
        // and a full log scan per keystroke (R7). The field-name list is itself cached per snapshot, so the
        // membership check is cheap and does not re-scan.
        if (!GetEventDataFieldNames(cacheKey, events).Contains(fieldName, StringComparer.Ordinal))
        {
            return [];
        }

        var perSnapshot = s_fieldValueCache.GetValue(
            cacheKey,
            static _ => new ConcurrentDictionary<string, Lazy<ImmutableArray<string>>>(StringComparer.Ordinal));

        return perSnapshot
            .GetOrAdd(
                fieldName,
                name => new Lazy<ImmutableArray<string>>(
                    () => [.. events.GetEventDataFieldValues(name).Distinct().Order(StringComparer.Ordinal)]))
            .Value;
    }

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
    internal static void Clear()
    {
        s_cache.Clear();
        s_fieldNameCache.Clear();
        s_fieldValueCache.Clear();
    }

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

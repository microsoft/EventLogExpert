// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Concurrent;

namespace EventLogExpert.Eventing.EventResolvers;

public sealed class EventResolverCache : IEventResolverCache
{
    private readonly ConcurrentDictionary<string, string> _descriptionCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _valueCache = new(StringComparer.Ordinal);

    public void ClearAll()
    {
        _descriptionCache.Clear();
        _valueCache.Clear();
    }

    /// <summary>Returns the description if it exists in the cache, otherwise adds it to the cache and returns it.</summary>
    public string GetOrAddDescription(string description) => _descriptionCache.GetOrAdd(description, static key => key);

    /// <summary>Returns the value if it exists in the cache, otherwise adds it to the cache and returns it.</summary>
    public string GetOrAddValue(string value) => _valueCache.GetOrAdd(value, static key => key);
}

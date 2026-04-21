// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Bounded LRU cache that resolves and caches event XML on demand. Concurrent requests
///     for the same key are coalesced through a shared <see cref="Lazy{T}" /> task so the
///     underlying <c>EvtQuery</c> / <c>RenderEventXml</c> P/Invoke pair runs at most once
///     per cache entry. Eviction is silent: a request for an evicted key triggers a fresh
///     resolve and re-cache.
/// </summary>
public class EventXmlResolver : IEventXmlResolver
{
    private const int DefaultInitialCapacity = 256;
    private const int DefaultMaxCapacity = 4096;

    private readonly Dictionary<XmlCacheKey, LinkedListNode<CacheEntry>> _cache;
    private readonly Lock _cacheLock = new();
    private readonly LinkedList<CacheEntry> _lruOrder = [];
    private readonly int _maxCapacity;

    private int _capacity;

    public EventXmlResolver() : this(DefaultInitialCapacity, DefaultMaxCapacity) { }

    protected EventXmlResolver(int initialCapacity, int maxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCapacity, initialCapacity);

        _capacity = initialCapacity;
        _maxCapacity = maxCapacity;
        _cache = new Dictionary<XmlCacheKey, LinkedListNode<CacheEntry>>(initialCapacity);
    }

    public void ClearAll()
    {
        using (_cacheLock.EnterScope())
        {
            _cache.Clear();
            _lruOrder.Clear();
        }
    }

    public void ClearLog(string owningLog)
    {
        if (string.IsNullOrEmpty(owningLog)) { return; }

        using (_cacheLock.EnterScope())
        {
            var node = _lruOrder.First;

            while (node is not null)
            {
                var next = node.Next;

                if (string.Equals(node.Value.Key.OwningLog, owningLog, StringComparison.Ordinal))
                {
                    _cache.Remove(node.Value.Key);
                    _lruOrder.Remove(node);
                }

                node = next;
            }
        }
    }

    public ValueTask<string> GetXmlAsync(DisplayEventModel evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // Already pre-rendered (log opened with renderXml: true). No cache traffic needed.
        if (!string.IsNullOrEmpty(evt.Xml))
        {
            return new ValueTask<string>(evt.Xml);
        }

        if (evt.RecordId is null || string.IsNullOrEmpty(evt.OwningLog))
        {
            return new ValueTask<string>(string.Empty);
        }

        var key = new XmlCacheKey(evt.OwningLog, evt.RecordId.Value, evt.PathType);
        Lazy<Task<string>> lazy = GetOrCreateEntry(key);

        return AwaitLazyAsync(lazy, cancellationToken);
    }

    /// <summary>Performs the actual EvtQuery / EvtNext / RenderEventXml work. Virtual for testability.</summary>
    protected virtual string ResolveXml(string owningLog, long recordId, PathType pathType)
    {
        using EvtHandle handle = EventMethods.EvtQuery(
            EventLogSession.GlobalSession.Handle,
            owningLog,
            $"*[System[EventRecordID='{recordId}']]",
            pathType);

        if (handle.IsInvalid) { return string.Empty; }

        var buffer = new IntPtr[1];
        int count = 0;

        bool success = EventMethods.EvtNext(handle, buffer.Length, buffer, 0, 0, ref count);

        if (!success || count == 0) { return string.Empty; }

        using EvtHandle eventHandle = new(buffer[0]);

        if (eventHandle.IsInvalid) { return string.Empty; }

        return EventMethods.RenderEventXml(eventHandle) ?? string.Empty;
    }

    private static async ValueTask<string> AwaitLazyAsync(Lazy<Task<string>> lazy, CancellationToken cancellationToken)
    {
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller-side cancel: leave the entry in place so other waiters can observe the result.
            throw;
        }
    }

    private void EnsureCapacity()
    {
        if (_cache.Count <= _capacity) { return; }

        // Auto-grow up to the hard cap before evicting. This is silent (not user-configurable)
        // so workloads with large active windows aren't punished by thrashing.
        if (_capacity < _maxCapacity)
        {
            _capacity = Math.Min(_capacity * 2, _maxCapacity);

            return;
        }

        // At hard cap — evict least-recently-used entries until back at capacity.
        while (_cache.Count > _capacity && _lruOrder.First is { } oldest)
        {
            _cache.Remove(oldest.Value.Key);
            _lruOrder.RemoveFirst();
        }
    }

    private void EvictIfMatches(XmlCacheKey key)
    {
        using (_cacheLock.EnterScope())
        {
            if (_cache.Remove(key, out var node))
            {
                _lruOrder.Remove(node);
            }
        }
    }

    private Lazy<Task<string>> GetOrCreateEntry(XmlCacheKey key)
    {
        using (_cacheLock.EnterScope())
        {
            if (_cache.TryGetValue(key, out var existingNode))
            {
                _lruOrder.Remove(existingNode);
                _lruOrder.AddLast(existingNode);

                return existingNode.Value.Lazy;
            }

            var lazy = new Lazy<Task<string>>(
                () => ResolveAndEvictOnFailureAsync(key),
                LazyThreadSafetyMode.ExecutionAndPublication);

            var entry = new CacheEntry(key, lazy);
            var node = _lruOrder.AddLast(entry);
            _cache[key] = node;

            EnsureCapacity();

            return lazy;
        }
    }

    private async Task<string> ResolveAndEvictOnFailureAsync(XmlCacheKey key)
    {
        try
        {
            return await Task.Run(() => ResolveXml(key.OwningLog, key.RecordId, key.PathType)).ConfigureAwait(false);
        }
        catch
        {
            EvictIfMatches(key);

            throw;
        }
    }

    private readonly record struct XmlCacheKey(string OwningLog, long RecordId, PathType PathType);

    private sealed record CacheEntry(XmlCacheKey Key, Lazy<Task<string>> Lazy);
}

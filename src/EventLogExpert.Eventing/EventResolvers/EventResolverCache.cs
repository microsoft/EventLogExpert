// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.EventResolvers;

public sealed class EventResolverCache : IEventResolverCache
{
    private readonly HashSet<string> _descriptionCache = [];
    private readonly ReaderWriterLockSlim _descriptionCacheLock = new();
    private readonly HashSet<string> _valueCache = [];
    private readonly ReaderWriterLockSlim _valueCacheLock = new();

    public void ClearAll()
    {
        _descriptionCacheLock.EnterWriteLock();
        _valueCacheLock.EnterWriteLock();

        try
        {
            _descriptionCache.Clear();
            _valueCache.Clear();
        }
        finally
        {
            _descriptionCacheLock.ExitWriteLock();
            _valueCacheLock.ExitWriteLock();
        }
    }

    /// <summary>Returns the description if it exists in the cache, otherwise adds it to the cache and returns it.</summary>
    public string GetOrAddDescription(string description)
    {
        _descriptionCacheLock.EnterUpgradeableReadLock();

        try
        {
            if (_descriptionCache.TryGetValue(description, out string? result))
            {
                return result;
            }

            _descriptionCacheLock.EnterWriteLock();

            try
            {
                if (_descriptionCache.TryGetValue(description, out result))
                {
                    return result;
                }

                _descriptionCache.Add(description);

                return description;
            }
            finally
            {
                _descriptionCacheLock.ExitWriteLock();
            }
        }
        finally
        {
            _descriptionCacheLock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>Returns the value if it exists in the cache, otherwise adds it to the cache and returns it.</summary>
    public string GetOrAddValue(string value)
    {
        _valueCacheLock.EnterUpgradeableReadLock();

        try
        {
            if (_valueCache.TryGetValue(value, out string? result))
            {
                return result;
            }

            _valueCacheLock.EnterWriteLock();

            try
            {
                // Double-check if the value was added by another thread
                if (_valueCache.TryGetValue(value, out result))
                {
                    return result;
                }

                _valueCache.Add(value);

                return value;
            }
            finally
            {
                _valueCacheLock.ExitWriteLock();
            }
        }
        finally
        {
            _valueCacheLock.ExitUpgradeableReadLock();
        }
    }
}

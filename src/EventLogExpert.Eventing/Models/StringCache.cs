// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Models;

public readonly record struct StringCache()
{
    private readonly HashSet<string> _cache = [];
    private readonly ReaderWriterLockSlim _cacheLock = new();

    /// <summary>Returns the value if it exists in the cache, otherwise adds it to the cache and returns it.</summary>
    public string Get(string value)
    {
        _cacheLock.EnterUpgradeableReadLock();

        try
        {
            if (_cache.TryGetValue(value, out string? result))
            {
                return result;
            }

            _cacheLock.EnterWriteLock();

            try
            {
                _cache.Add(value);

                return value;
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }
        finally
        {
            _cacheLock.ExitUpgradeableReadLock();
        }
    }
}

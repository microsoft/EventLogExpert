// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Concurrent;

namespace EventLogExpert.Eventing.Helpers;

internal static class ResolverMethods
{
    internal const int MaxCacheSize = 4096;

    private static ConcurrentDictionary<uint, string> s_hResultCache = new();
    private static ConcurrentDictionary<uint, string> s_ntStatusCache = new();

    /// <summary>
    ///     Resolves an HRESULT or Win32 error code to a human-readable string.
    ///     Uses the system message table via FormatMessage, falling back to ntdll.dll's message table
    ///     for codes not found in the system table (e.g., NTSTATUS codes).
    ///     Results are cached to avoid repeated P/Invoke calls.
    /// </summary>
    internal static string GetErrorMessage(uint hResult) =>
        GetOrAddBounded(ref s_hResultCache, hResult, static code =>
            NativeMethods.FormatSystemMessage(code) ??
            NativeMethods.FormatNtStatusMessage(code) ??
            $"0x{code:X8}");

    /// <summary>Resolves an NTSTATUS code to a human-readable string.</summary>
    internal static string GetNtStatusMessage(uint ntStatus) =>
        GetOrAddBounded(ref s_ntStatusCache, ntStatus, static status =>
            NativeMethods.FormatNtStatusMessage(status) ??
            NativeMethods.FormatSystemMessage(status) ??
            $"0x{status:X8}");

    /// <summary>
    ///     Bounded cache lookup with atomic swap eviction. On a cache hit the entry is returned
    ///     immediately regardless of cache size. On a miss, if the cache has reached
    ///     <see cref="MaxCacheSize"/> the entire dictionary is atomically swapped with a fresh
    ///     instance (only one thread performs the swap) before inserting the new entry.
    /// </summary>
    private static string GetOrAddBounded(
        ref ConcurrentDictionary<uint, string> cache,
        uint key,
        Func<uint, string> factory)
    {
        var snapshot = Volatile.Read(ref cache);

        if (snapshot.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (snapshot.Count < MaxCacheSize)
        {
            return Volatile.Read(ref cache).GetOrAdd(key, factory);
        }

        var replacement = new ConcurrentDictionary<uint, string>();
        Interlocked.CompareExchange(ref cache, replacement, snapshot);

        return Volatile.Read(ref cache).GetOrAdd(key, factory);
    }
}

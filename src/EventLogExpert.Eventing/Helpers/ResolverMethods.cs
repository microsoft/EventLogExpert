// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Concurrent;

namespace EventLogExpert.Eventing.Helpers;

internal static class ResolverMethods
{
    private static readonly ConcurrentDictionary<uint, string> s_hResultCache = [];
    private static readonly ConcurrentDictionary<uint, string> s_ntStatusCache = [];

    /// <summary>
    ///     Resolves an HRESULT or Win32 error code to a human-readable string.
    ///     Uses the system message table via FormatMessage, falling back to ntdll.dll's message table
    ///     for codes not found in the system table (e.g., NTSTATUS codes).
    ///     Results are cached to avoid repeated P/Invoke calls.
    /// </summary>
    internal static string GetErrorMessage(uint hResult) =>
        s_hResultCache.GetOrAdd(hResult, static code =>
            NativeMethods.FormatSystemMessage(code) ??
            NativeMethods.FormatNtStatusMessage(code) ??
            $"0x{code:X8}");

    /// <summary>Resolves an NTSTATUS code to a human-readable string.</summary>
    internal static string GetNtStatusMessage(uint ntStatus) =>
        s_ntStatusCache.GetOrAdd(ntStatus, static status =>
            NativeMethods.FormatNtStatusMessage(status) ??
            NativeMethods.FormatSystemMessage(status) ??
            $"0x{status:X8}");
}

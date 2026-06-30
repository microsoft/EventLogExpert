// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Read-only navigation over an offline image's registry hive, mirroring the small slice of
///     <see cref="Microsoft.Win32.RegistryKey" /> the offline readers use. Implemented by the managed <c>regf</c> parser (
///     <see cref="OfflineHiveFile" />) so package identity / registry virtualization never apply, and by
///     <see cref="LiveRegistryKeyAdapter" /> over a live key for the host provenance path.
/// </summary>
internal interface IOfflineRegistryKey : IDisposable
{
    /// <summary>Immediate child key names in stored (RegEnumKeyEx) order; never re-sorted.</summary>
    IReadOnlyList<string> GetSubKeyNames();

    /// <summary>
    ///     Reads a value by name (<see langword="null" />/empty = the key's default value), returning the SAME CLR types
    ///     as <see cref="Microsoft.Win32.RegistryKey.GetValue(string)" /> so caller <c>as string</c> / <c>is int</c> logic is
    ///     identical: <c>REG_SZ</c>/<c>REG_EXPAND_SZ</c> to <see cref="string" /> (literal, never environment-expanded),
    ///     <c>REG_DWORD</c> to <see cref="int" />, <c>REG_QWORD</c> to <see cref="long" />, <c>REG_MULTI_SZ</c> to
    ///     <see cref="string" />[], anything else to <see cref="byte" />[]. <see langword="null" /> when the value is absent.
    /// </summary>
    object? GetValue(string? name);

    /// <summary>
    ///     Opens a descendant key by a backslash-separated, case-insensitive relative <paramref name="path" />; returns
    ///     <see langword="null" /> when any segment is absent.
    /// </summary>
    IOfflineRegistryKey? OpenSubKey(string path);
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

internal interface IOfflineRegistryKey : IDisposable
{
    // First channel wins: child names stay in registry enumeration order.
    IReadOnlyList<string> GetSubKeyNames();

    // Mirror RegistryKey.GetValue CLR types; REG_DWORD must be boxed Int32.
    object? GetValue(string? name);

    IOfflineRegistryKey? OpenSubKey(string path);
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

internal sealed class OfflineHiveKey(OfflineHiveFile hive, uint cellOffset) : IOfflineRegistryKey
{
    // No-op: OfflineHiveFile owns the mapping; a cursor owns nothing.
    public void Dispose() { }

    public IReadOnlyList<string> GetSubKeyNames() => hive.GetSubKeyNamesFrom(cellOffset);

    public object? GetValue(string? name) => hive.GetValueFrom(cellOffset, name);

    public IOfflineRegistryKey? OpenSubKey(string path) => hive.OpenSubKeyFrom(cellOffset, path);
}

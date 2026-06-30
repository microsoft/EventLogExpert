// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     A lightweight cursor at one key inside an <see cref="OfflineHiveFile" />. All reads delegate back to the
///     owning hive against this cursor's cell offset. <see cref="Dispose" /> is intentionally a no-op: the
///     <see cref="OfflineHiveFile" /> owns the memory mapping, so disposing a subkey cursor (as the readers do per
///     <c>using</c>) must NOT tear down the shared view.
/// </summary>
internal sealed class OfflineHiveKey(OfflineHiveFile hive, uint cellOffset) : IOfflineRegistryKey
{
    public void Dispose() { /* The OfflineHiveFile owns the mapping; a subkey cursor owns nothing. */ }

    public IReadOnlyList<string> GetSubKeyNames() => hive.GetSubKeyNamesFrom(cellOffset);

    public object? GetValue(string? name) => hive.GetValueFrom(cellOffset, name);

    public IOfflineRegistryKey? OpenSubKey(string path) => hive.OpenSubKeyFrom(cellOffset, path);
}

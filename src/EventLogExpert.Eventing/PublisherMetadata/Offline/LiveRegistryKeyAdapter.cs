// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Adapts a live <see cref="RegistryKey" /> to <see cref="IOfflineRegistryKey" /> so the host (live-build)
///     provenance path shares the single parse routine with the offline path. <see cref="GetValue" /> reads with
///     <see cref="RegistryValueOptions.DoNotExpandEnvironmentNames" />, preserving the host reader's exact behavior.
/// </summary>
internal sealed class LiveRegistryKeyAdapter(RegistryKey key, bool ownsKey) : IOfflineRegistryKey
{
    public void Dispose()
    {
        if (ownsKey) { key.Dispose(); }
    }

    public IReadOnlyList<string> GetSubKeyNames() => key.GetSubKeyNames();

    public object? GetValue(string? name) => key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

    public IOfflineRegistryKey? OpenSubKey(string path)
    {
        RegistryKey? subKey = key.OpenSubKey(path);

        return subKey is null ? null : new LiveRegistryKeyAdapter(subKey, ownsKey: true);
    }
}

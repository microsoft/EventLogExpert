// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32;

namespace EventLogExpert.Eventing.OfflineImaging.Registry;

internal sealed class LiveRegistryKeyAdapter(RegistryKey key, bool ownsKey) : IOfflineRegistryKey
{
    public void Dispose()
    {
        if (ownsKey) { key.Dispose(); }
    }

    public IReadOnlyList<string> GetSubKeyNames() => key.GetSubKeyNames();

    // Preserve literal REG_EXPAND_SZ behavior; do not expand host environment variables.
    public object? GetValue(string? name) => key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

    public IOfflineRegistryKey? OpenSubKey(string path)
    {
        RegistryKey? subKey = key.OpenSubKey(path);

        return subKey is null ? null : new LiveRegistryKeyAdapter(subKey, ownsKey: true);
    }
}

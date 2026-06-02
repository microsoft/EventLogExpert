// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.Adapters.Settings;

internal sealed class MauiLegacyPreferencesAdapter : ILegacyPreferences
{
    public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);

    public string? GetString(string key) => Preferences.Default.Get<string?>(key, null);

    public void Remove(string key)
    {
        if (Preferences.Default.ContainsKey(key)) { Preferences.Default.Remove(key); }
    }

    public void SetString(string key, string value) => Preferences.Default.Set(key, value);
}

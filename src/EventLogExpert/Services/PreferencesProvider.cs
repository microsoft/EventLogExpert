// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using System.Text.Json;

namespace EventLogExpert.Services;

public class PreferencesProvider : IPreferencesProvider
{
    private const string _disabledDatabasesPreference = "disabled-databases";
    private const string _prereleasePreference = "prerelease-enabled";

    public IList<string> DisabledDatabasesPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(_disabledDatabasesPreference, "[]")) ?? new List<string>();
        set => Preferences.Default.Set(_disabledDatabasesPreference, JsonSerializer.Serialize(value));
    }

    public bool PrereleasePreference
    {
        get => Preferences.Default.Get(_prereleasePreference, false);
        set => Preferences.Default.Set(_prereleasePreference, value);
    }
}

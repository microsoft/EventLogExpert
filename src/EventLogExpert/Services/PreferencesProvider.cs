// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using System.Text.Json;

namespace EventLogExpert.Services;

public class PreferencesProvider : IPreferencesProvider
{
    private const string DisabledDatabases = "disabled-databases";
    private const string DisplaySelectionEnabled = "display-selection-enabled";
    private const string PrereleaseEnabled = "prerelease-enabled";
    private const string TimeZone = "timezone";

    public IList<string> DisabledDatabasesPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(DisabledDatabases, "[]")) ?? new List<string>();
        set => Preferences.Default.Set(DisabledDatabases, JsonSerializer.Serialize(value));
    }

    public bool DisplayPaneSelectionPreference
    {
        get => Preferences.Default.Get(DisplaySelectionEnabled, false);
        set => Preferences.Default.Set(DisplaySelectionEnabled, value);
    }

    public bool PrereleasePreference
    {
        get => Preferences.Default.Get(PrereleaseEnabled, false);
        set => Preferences.Default.Set(PrereleaseEnabled, value);
    }

    public string TimeZonePreference
    {
        get => Preferences.Default.Get(TimeZone, TimeZoneInfo.Local.Id);
        set => Preferences.Default.Set(TimeZone, value);
    }
}

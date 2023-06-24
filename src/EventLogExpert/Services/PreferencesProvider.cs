// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EventLogExpert.Services;

public class PreferencesProvider : IPreferencesProvider
{
    private const string DisabledDatabases = "disabled-databases";
    private const string DisplaySelectionEnabled = "display-selection-enabled";
    private const string FavoriteFilters = "favorite-filters";
    private const string LoggingLevel = "logging-level";
    private const string PrereleaseEnabled = "prerelease-enabled";
    private const string RecentFilters = "recent-filters";
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

    public IList<string> FavoriteFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(FavoriteFilters, "[]")) ?? new List<string>();
        set => Preferences.Default.Set(FavoriteFilters, JsonSerializer.Serialize(value));
    }

    public LogLevel LogLevelPreference
    {
        get => Preferences.Default.Get(LoggingLevel, LogLevel.Information);
        set => Preferences.Default.Set(LoggingLevel, value);
    }

    public bool PrereleasePreference
    {
        get => Preferences.Default.Get(PrereleaseEnabled, false);
        set => Preferences.Default.Set(PrereleaseEnabled, value);
    }

    public IList<string> RecentFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(RecentFilters, "[]")) ?? new List<string>();
        set => Preferences.Default.Set(RecentFilters, JsonSerializer.Serialize(value));
    }

    public string TimeZonePreference
    {
        get => Preferences.Default.Get(TimeZone, TimeZoneInfo.Local.Id);
        set => Preferences.Default.Set(TimeZone, value);
    }
}

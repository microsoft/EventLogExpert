// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EventLogExpert.Services;

public sealed class PreferencesProvider : IPreferencesProvider
{
    private const string ActivityIdColumn = "activity-id-column";
    private const string ComputerNameColumn = "computer-name-column";
    private const string DateAndTimeColumn = "date-and-time-column";
    private const string DisabledDatabases = "disabled-databases";
    private const string DisplaySelectionEnabled = "display-selection-enabled";
    private const string EventIdColumn = "event-id-column";
    private const string FavoriteFilters = "favorite-filters";
    private const string KeyboardCopyType = "keyboard-copy-type";
    private const string LevelColumn = "level-column";
    private const string LoggingLevel = "logging-level";
    private const string LogNameColumn = "log-name-column";
    private const string PrereleaseEnabled = "prerelease-enabled";
    private const string RecentFilters = "recent-filters";
    private const string SavedFilters = "saved-filters";
    private const string SourceColumn = "source-column";
    private const string TaskCategoryColumn = "task-category-column";
    private const string TimeZone = "timezone";

    public bool ActivityIdColumnPreference
    {
        get => Preferences.Default.Get(ActivityIdColumn, false);
        set => Preferences.Default.Set(ActivityIdColumn, value);
    }

    public bool ComputerNameColumnPreference
    {
        get => Preferences.Default.Get(ComputerNameColumn, false);
        set => Preferences.Default.Set(ComputerNameColumn, value);
    }

    public bool DateAndTimeColumnPreference
    {
        get => Preferences.Default.Get(DateAndTimeColumn, true);
        set => Preferences.Default.Set(DateAndTimeColumn, value);
    }

    public IList<string> DisabledDatabasesPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(DisabledDatabases, "[]")) ?? [];
        set => Preferences.Default.Set(DisabledDatabases, JsonSerializer.Serialize(value));
    }

    public bool DisplayPaneSelectionPreference
    {
        get => Preferences.Default.Get(DisplaySelectionEnabled, false);
        set => Preferences.Default.Set(DisplaySelectionEnabled, value);
    }

    public bool EventIdColumnPreference
    {
        get => Preferences.Default.Get(EventIdColumn, true);
        set => Preferences.Default.Set(EventIdColumn, value);
    }

    public IList<string> FavoriteFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(FavoriteFilters, "[]")) ?? [];
        set => Preferences.Default.Set(FavoriteFilters, JsonSerializer.Serialize(value));
    }

    public CopyType KeyboardCopyTypePreference {
        get => Enum.TryParse(Preferences.Default.Get(KeyboardCopyType, CopyType.Full.ToString()),
            out CopyType value) ?
            value : CopyType.Full;
        set => Preferences.Default.Set(KeyboardCopyType, value.ToString());
    }

    public bool LevelColumnPreference
    {
        get => Preferences.Default.Get(LevelColumn, true);
        set => Preferences.Default.Set(LevelColumn, value);
    }

    public LogLevel LogLevelPreference
    {
        get => Enum.TryParse(Preferences.Default.Get(LoggingLevel, LogLevel.Information.ToString()),
            out LogLevel value) ?
            value : LogLevel.Information;
        set => Preferences.Default.Set(LoggingLevel, value.ToString());
    }

    public bool LogNameColumnPreference
    {
        get => Preferences.Default.Get(LogNameColumn, false);
        set => Preferences.Default.Set(LogNameColumn, value);
    }

    public bool PrereleasePreference
    {
        get => Preferences.Default.Get(PrereleaseEnabled, false);
        set => Preferences.Default.Set(PrereleaseEnabled, value);
    }

    public IList<string> RecentFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(RecentFilters, "[]")) ?? [];
        set => Preferences.Default.Set(RecentFilters, JsonSerializer.Serialize(value));
    }

    public IList<FilterGroupModel> SavedFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<FilterGroupModel>>(Preferences.Default.Get(SavedFilters, "[]")) ?? [];
        set => Preferences.Default.Set(SavedFilters, JsonSerializer.Serialize(value));
    }

    public bool SourceColumnPreference
    {
        get => Preferences.Default.Get(SourceColumn, true);
        set => Preferences.Default.Set(SourceColumn, value);
    }

    public bool TaskCategoryColumnPreference
    {
        get => Preferences.Default.Get(TaskCategoryColumn, true);
        set => Preferences.Default.Set(TaskCategoryColumn, value);
    }

    public string TimeZonePreference
    {
        get => Preferences.Default.Get(TimeZone, TimeZoneInfo.Local.Id);
        set => Preferences.Default.Set(TimeZone, value);
    }
}

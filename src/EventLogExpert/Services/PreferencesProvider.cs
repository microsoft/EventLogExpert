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
    private const string DisabledDatabases = "disabled-databases";
    private const string DisplaySelectionEnabled = "display-selection-enabled";
    private const string EnabledEventTableColumns = "enabled-event-table-columns";
    private const string FavoriteFilters = "favorite-filters";
    private const string KeyboardCopyType = "keyboard-copy-type";
    private const string LoggingLevel = "logging-level";
    private const string PreReleaseEnabled = "prerelease-enabled";
    private const string RecentFilters = "recent-filters";
    private const string SavedFilters = "saved-filters";
    private const string TimeZone = "timezone";

    public IEnumerable<string> DisabledDatabasesPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(DisabledDatabases, "[]")) ?? [];
        set => Preferences.Default.Set(DisabledDatabases, JsonSerializer.Serialize(value));
    }

    public bool DisplayPaneSelectionPreference
    {
        get => Preferences.Default.Get(DisplaySelectionEnabled, false);
        set => Preferences.Default.Set(DisplaySelectionEnabled, value);
    }

    public IEnumerable<ColumnName> EnabledEventTableColumnsPreference
    {
        get =>
            JsonSerializer.Deserialize<List<ColumnName>>(
                Preferences.Default.Get(
                    EnabledEventTableColumns,
                    $"[{ColumnName.Level:D}, {ColumnName.DateAndTime:D}, {ColumnName.Source:D}, {ColumnName.EventId:D}, {ColumnName.TaskCategory:D}]")) ??
            [];
        set => Preferences.Default.Set(EnabledEventTableColumns, JsonSerializer.Serialize(value));
    }

    public IEnumerable<string> FavoriteFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(FavoriteFilters, "[]")) ?? [];
        set => Preferences.Default.Set(FavoriteFilters, JsonSerializer.Serialize(value));
    }

    public CopyType KeyboardCopyTypePreference
    {
        get => Enum.TryParse(Preferences.Default.Get(KeyboardCopyType, CopyType.Full.ToString()),
            out CopyType value) ?
            value : CopyType.Full;
        set => Preferences.Default.Set(KeyboardCopyType, value.ToString());
    }

    public LogLevel LogLevelPreference
    {
        get => Enum.TryParse(Preferences.Default.Get(LoggingLevel, LogLevel.Information.ToString()),
            out LogLevel value) ?
            value : LogLevel.Information;
        set => Preferences.Default.Set(LoggingLevel, value.ToString());
    }

    public bool PreReleasePreference
    {
        get => Preferences.Default.Get(PreReleaseEnabled, false);
        set => Preferences.Default.Set(PreReleaseEnabled, value);
    }

    public IEnumerable<string> RecentFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(RecentFilters, "[]")) ?? [];
        set => Preferences.Default.Set(RecentFilters, JsonSerializer.Serialize(value));
    }

    public IEnumerable<FilterGroupModel> SavedFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<FilterGroupModel>>(Preferences.Default.Get(SavedFilters, "[]")) ?? [];
        set => Preferences.Default.Set(SavedFilters, JsonSerializer.Serialize(value));
    }

    public string TimeZonePreference
    {
        get => Preferences.Default.Get(TimeZone, TimeZoneInfo.Local.Id);
        set => Preferences.Default.Set(TimeZone, value);
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Store.Settings;

public record SettingsAction
{
    public record CheckForUpdates : SettingsAction;

    public record LoadDatabases : SettingsAction;

    public record LoadDatabasesCompleted(IEnumerable<string> loadedDatabases) : SettingsAction;

    public record LoadSettings : SettingsAction;

    public record LoadSettingsCompleted(SettingsModel config) : SettingsAction;

    public record OpenMenu : SettingsAction;

    public record Save(SettingsModel Settings) : SettingsAction;

    public record SaveCompleted(SettingsModel Settings) : SettingsAction;

    public record ToggleShowLogName() : SettingsAction;

    public record ToggleShowComputerName() : SettingsAction;
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Store.Settings;

public record SettingsAction
{
    public record CheckForUpdates : SettingsAction;

    public record LoadProviders(string Path) : SettingsAction;

    public record LoadSettings(string Path) : SettingsAction;

    public record OpenMenu : SettingsAction;

    public record Save(SettingsModel Settings, string Path) : SettingsAction;
}

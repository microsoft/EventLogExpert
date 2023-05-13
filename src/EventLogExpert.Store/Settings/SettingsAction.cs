// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Store.Settings;

public record SettingsAction
{
    public record Load : SettingsAction;

    public record OpenMenu : SettingsAction;

    public record Save(SettingsModel Settings, string Path) : SettingsAction;
}

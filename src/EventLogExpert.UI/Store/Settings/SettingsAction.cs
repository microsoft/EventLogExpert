﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.Settings;

public sealed record SettingsAction
{
    public sealed record LoadDatabases;

    public sealed record LoadDatabasesCompleted(IEnumerable<string> LoadedDatabases);

    public sealed record LoadSettings;

    public sealed record LoadSettingsCompleted(SettingsModel Config, IList<string> DisabledDatabases);

    public sealed record OpenDebugLog;

    public sealed record OpenMenu;

    public sealed record Save(SettingsModel Settings);

    public sealed record SaveCompleted(SettingsModel Settings);

    public sealed record SaveDisabledDatabases(IList<string> Databases);

    public sealed record SaveDisabledDatabasesCompleted(IList<string> Databases);
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using Fluxor;

namespace EventLogExpert.UI.Store.Settings;

public sealed class SettingsEffects(
    IPreferencesProvider preferencesProvider,
    IEnabledDatabaseCollectionProvider enabledDatabaseCollectionProvider)
{
    [EffectMethod(typeof(SettingsAction.LoadDatabases))]
    public Task HandleLoadDatabases(IDispatcher dispatcher)
    {
        var databases = enabledDatabaseCollectionProvider.GetEnabledDatabases();

        dispatcher.Dispatch(new SettingsAction.LoadDatabasesCompleted(databases));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(SettingsAction.LoadSettings))]
    public Task HandleLoadSettings(IDispatcher dispatcher)
    {
        SettingsModel config = new()
        {
            TimeZoneId = preferencesProvider.TimeZonePreference,
            DisabledDatabases = preferencesProvider.DisabledDatabasesPreference,
            ShowDisplayPaneOnSelectionChange = preferencesProvider.DisplayPaneSelectionPreference,
            CopyType = preferencesProvider.KeyboardCopyTypePreference,
            LogLevel = preferencesProvider.LogLevelPreference,
            IsPreReleaseEnabled = preferencesProvider.PrereleasePreference
        };

        dispatcher.Dispatch(new SettingsAction.LoadSettingsCompleted(config));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSave(SettingsAction.Save action, IDispatcher dispatcher)
    {
        preferencesProvider.TimeZonePreference = action.Settings.TimeZoneId;
        preferencesProvider.DisabledDatabasesPreference = action.Settings.DisabledDatabases;
        preferencesProvider.DisplayPaneSelectionPreference = action.Settings.ShowDisplayPaneOnSelectionChange;
        preferencesProvider.KeyboardCopyTypePreference = action.Settings.CopyType;
        preferencesProvider.LogLevelPreference = action.Settings.LogLevel;
        preferencesProvider.PrereleasePreference = action.Settings.IsPreReleaseEnabled;

        dispatcher.Dispatch(new SettingsAction.SaveCompleted(action.Settings));

        return Task.CompletedTask;
    }
}

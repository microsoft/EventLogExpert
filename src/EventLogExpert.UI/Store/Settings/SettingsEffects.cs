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
            ShowDisplayPaneOnSelectionChange = preferencesProvider.DisplayPaneSelectionPreference,
            CopyType = preferencesProvider.KeyboardCopyTypePreference,
            LogLevel = preferencesProvider.LogLevelPreference,
            IsPreReleaseEnabled = preferencesProvider.PreReleasePreference
        };

        dispatcher.Dispatch(new SettingsAction.LoadSettingsCompleted(config, preferencesProvider.DisabledDatabasesPreference));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSave(SettingsAction.Save action, IDispatcher dispatcher)
    {
        preferencesProvider.TimeZonePreference = action.Settings.TimeZoneId;
        preferencesProvider.DisplayPaneSelectionPreference = action.Settings.ShowDisplayPaneOnSelectionChange;
        preferencesProvider.KeyboardCopyTypePreference = action.Settings.CopyType;
        preferencesProvider.LogLevelPreference = action.Settings.LogLevel;
        preferencesProvider.PreReleasePreference = action.Settings.IsPreReleaseEnabled;

        dispatcher.Dispatch(new SettingsAction.SaveCompleted(action.Settings));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSaveDisabledDatabases(SettingsAction.SaveDisabledDatabases action, IDispatcher dispatcher)
    {
        preferencesProvider.DisabledDatabasesPreference = action.Databases;
        
        dispatcher.Dispatch(new SettingsAction.SaveDisabledDatabasesCompleted(action.Databases));

        return Task.CompletedTask;
    }
}

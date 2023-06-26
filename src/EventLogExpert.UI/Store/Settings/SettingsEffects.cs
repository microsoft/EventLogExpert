// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using Fluxor;

namespace EventLogExpert.UI.Store.Settings;

public class SettingsEffects
{
    private readonly IPreferencesProvider _preferencesProvider;
    private readonly IEnabledDatabaseCollectionProvider _enabledDatabaseCollectionProvider;

    public SettingsEffects(
        IPreferencesProvider preferencesProvider,
        IEnabledDatabaseCollectionProvider enabledDatabaseCollectionProvider)
    {
        _preferencesProvider = preferencesProvider;
        _enabledDatabaseCollectionProvider = enabledDatabaseCollectionProvider;
    }

    [EffectMethod]
    public async Task HandleLoadDatabases(SettingsAction.LoadDatabases action, IDispatcher dispatcher)
    {
        var databases = _enabledDatabaseCollectionProvider.GetEnabledDatabases();

        dispatcher.Dispatch(new SettingsAction.LoadDatabasesCompleted(databases));
    }

    [EffectMethod]
    public async Task HandleLoadSettings(SettingsAction.LoadSettings action, IDispatcher dispatcher)
    {
        SettingsModel config = new()
        {
            TimeZoneId = _preferencesProvider.TimeZonePreference,
            DisabledDatabases = _preferencesProvider.DisabledDatabasesPreference,
            ShowDisplayPaneOnSelectionChange = _preferencesProvider.DisplayPaneSelectionPreference,
            LogLevel = _preferencesProvider.LogLevelPreference,
            IsPrereleaseEnabled = _preferencesProvider.PrereleasePreference
        };

        dispatcher.Dispatch(new SettingsAction.LoadSettingsCompleted(config));
    }

    [EffectMethod]
    public async Task HandleSave(SettingsAction.Save action, IDispatcher dispatcher)
    {
        _preferencesProvider.TimeZonePreference = action.Settings.TimeZoneId;
        _preferencesProvider.DisabledDatabasesPreference = action.Settings.DisabledDatabases;
        _preferencesProvider.DisplayPaneSelectionPreference = action.Settings.ShowDisplayPaneOnSelectionChange;
        _preferencesProvider.LogLevelPreference = action.Settings.LogLevel;
        _preferencesProvider.PrereleasePreference = action.Settings.IsPrereleaseEnabled;

        dispatcher.Dispatch(new SettingsAction.SaveCompleted(action.Settings));
    }
}

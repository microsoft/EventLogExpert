// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using Fluxor;

namespace EventLogExpert.UI.Store.Settings;

public sealed class SettingsEffects(
    IPreferencesProvider preferencesProvider,
    IEnabledDatabaseCollectionProvider enabledDatabaseCollectionProvider,
    IEventTableColumnProvider eventTableColumnProvider)
{
    [EffectMethod(typeof(SettingsAction.LoadColumns))]
    public Task HandleLoadColumns(IDispatcher dispatcher)
    {
        var columns = eventTableColumnProvider.GetColumns();

        dispatcher.Dispatch(new SettingsAction.LoadColumnsCompleted(columns));

        return Task.CompletedTask;
    }

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
            LogLevel = preferencesProvider.LogLevelPreference,
            IsPrereleaseEnabled = preferencesProvider.PrereleasePreference
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
        preferencesProvider.LogLevelPreference = action.Settings.LogLevel;
        preferencesProvider.PrereleasePreference = action.Settings.IsPrereleaseEnabled;

        dispatcher.Dispatch(new SettingsAction.SaveCompleted(action.Settings));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleToggleColumn(SettingsAction.ToggleColumn action, IDispatcher dispatcher)
    {
        switch (action.ColumnName)
        {
            case ColumnName.Level :
                preferencesProvider.LevelColumnPreference = !preferencesProvider.LevelColumnPreference;
                break;
            case ColumnName.DateAndTime :
                preferencesProvider.DateAndTimeColumnPreference = !preferencesProvider.DateAndTimeColumnPreference;
                break;
            case ColumnName.ActivityId :
                preferencesProvider.ActivityIdColumnPreference = !preferencesProvider.ActivityIdColumnPreference;
                break;
            case ColumnName.LogName :
                preferencesProvider.LogNameColumnPreference = !preferencesProvider.LogNameColumnPreference;
                break;
            case ColumnName.ComputerName :
                preferencesProvider.ComputerNameColumnPreference = !preferencesProvider.ComputerNameColumnPreference;
                break;
            case ColumnName.Source :
                preferencesProvider.SourceColumnPreference = !preferencesProvider.SourceColumnPreference;
                break;
            case ColumnName.EventId :
                preferencesProvider.EventIdColumnPreference = !preferencesProvider.EventIdColumnPreference;
                break;
            case ColumnName.TaskCategory :
                preferencesProvider.TaskCategoryColumnPreference = !preferencesProvider.TaskCategoryColumnPreference;
                break;
        }

        return Task.CompletedTask;
    }
}

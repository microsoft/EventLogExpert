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
    private readonly IEventTableColumnProvider _eventTableColumnProvider;

    public SettingsEffects(
        IPreferencesProvider preferencesProvider,
        IEnabledDatabaseCollectionProvider enabledDatabaseCollectionProvider,
        IEventTableColumnProvider eventTableColumnProvider)
    {
        _preferencesProvider = preferencesProvider;
        _enabledDatabaseCollectionProvider = enabledDatabaseCollectionProvider;
        _eventTableColumnProvider = eventTableColumnProvider;
    }

    [EffectMethod(typeof(SettingsAction.LoadColumns))]
    public async Task HandleLoadColumns(IDispatcher dispatcher)
    {
        var columns = _eventTableColumnProvider.GetColumns();

        dispatcher.Dispatch(new SettingsAction.LoadColumnsCompleted(columns));
    }

    [EffectMethod(typeof(SettingsAction.LoadDatabases))]
    public async Task HandleLoadDatabases(IDispatcher dispatcher)
    {
        var databases = _enabledDatabaseCollectionProvider.GetEnabledDatabases();

        dispatcher.Dispatch(new SettingsAction.LoadDatabasesCompleted(databases));
    }

    [EffectMethod(typeof(SettingsAction.LoadSettings))]
    public async Task HandleLoadSettings(IDispatcher dispatcher)
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

    [EffectMethod]
    public async Task HandleToggleColumn(SettingsAction.ToggleColumn action, IDispatcher dispatcher)
    {
        switch (action.ColumnName)
        {
            case ColumnName.Level :
                _preferencesProvider.LevelColumnPreference = !_preferencesProvider.LevelColumnPreference;
                break;
            case ColumnName.DateAndTime :
                _preferencesProvider.DateAndTimeColumnPreference = !_preferencesProvider.DateAndTimeColumnPreference;
                break;
            case ColumnName.ActivityId :
                _preferencesProvider.ActivityIdColumnPreference = !_preferencesProvider.ActivityIdColumnPreference;
                break;
            case ColumnName.LogName :
                _preferencesProvider.LogNameColumnPreference = !_preferencesProvider.LogNameColumnPreference;
                break;
            case ColumnName.ComputerName :
                _preferencesProvider.ComputerNameColumnPreference = !_preferencesProvider.ComputerNameColumnPreference;
                break;
            case ColumnName.Source :
                _preferencesProvider.SourceColumnPreference = !_preferencesProvider.SourceColumnPreference;
                break;
            case ColumnName.EventId :
                _preferencesProvider.EventIdColumnPreference = !_preferencesProvider.EventIdColumnPreference;
                break;
            case ColumnName.TaskCategory :
                _preferencesProvider.TaskCategoryColumnPreference = !_preferencesProvider.TaskCategoryColumnPreference;
                break;
            case ColumnName.Description :
                _preferencesProvider.DescriptionColumnPreference = !_preferencesProvider.DescriptionColumnPreference;
                break;
        }
    }
}

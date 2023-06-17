// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using Fluxor;

namespace EventLogExpert.UI.Store.Settings;

public class SettingsEffects
{
    private readonly FileLocationOptions _fileLocationOptions;
    private readonly IPreferencesProvider _preferencesProvider;
    private readonly ITraceLogger _traceLogger;

    public SettingsEffects(ITraceLogger traceLogger,
        FileLocationOptions fileLocationOptions,
        IPreferencesProvider preferencesProvider)
    {
        _traceLogger = traceLogger;
        _fileLocationOptions = fileLocationOptions;
        _preferencesProvider = preferencesProvider;
    }

    [EffectMethod]
    public async Task HandleLoadDatabases(SettingsAction.LoadDatabases action, IDispatcher dispatcher)
    {
        List<string> databases = new();

        try
        {
            if (Directory.Exists(_fileLocationOptions.DatabasePath))
            {
                foreach (var item in Directory.EnumerateFiles(_fileLocationOptions.DatabasePath, "*.db"))
                {
                    databases.Add(Path.GetFileName(item));
                }
            }
        }
        catch (Exception ex)
        {
            _traceLogger.Trace($"{nameof(SettingsEffects)}.{nameof(HandleLoadDatabases)} failed: {ex}");
            return;
        }

        if (databases.Count <= 0) { return; }

        var disabledDatabases = _preferencesProvider.DisabledDatabasesPreference;

        if (disabledDatabases?.Any() is true)
        {
            databases.RemoveAll(enabled => disabledDatabases
                .Any(disabled => string.Equals(enabled, disabled, StringComparison.InvariantCultureIgnoreCase)));
        }

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
        _preferencesProvider.PrereleasePreference = action.Settings.IsPrereleaseEnabled;

        dispatcher.Dispatch(new SettingsAction.SaveCompleted(action.Settings));
    }
}

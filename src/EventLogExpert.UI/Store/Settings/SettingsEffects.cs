// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using Fluxor;
using System.Text.Json;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store.Settings;

public class SettingsEffects
{
    private readonly ITraceLogger _traceLogger;
    private readonly FileLocationOptions _fileLocationOptions;
    private readonly IPreferencesProvider _preferencesProvider;
    private readonly IAlertDialogService _alertDialogService;
    private static readonly ReaderWriterLockSlim ConfigSrwLock = new();

    public SettingsEffects(ITraceLogger traceLogger, FileLocationOptions fileLocationOptions,
        IPreferencesProvider preferencesProvider, IAlertDialogService alertDialogService)
    {
        _traceLogger = traceLogger;
        _fileLocationOptions = fileLocationOptions;
        _preferencesProvider = preferencesProvider;
        _alertDialogService = alertDialogService;
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
        SettingsModel? config = ReadSettingsConfig();

        config ??= new();

        var disabledDatabases = _preferencesProvider.DisabledDatabasesPreference;

        if (disabledDatabases?.Any() is true)
        {
            config.DisabledDatabases = disabledDatabases;
        }

        config.IsPrereleaseEnabled = _preferencesProvider.PrereleasePreference;

        dispatcher.Dispatch(new SettingsAction.LoadSettingsCompleted(config));
    }

    [EffectMethod]
    public async Task HandleSave(SettingsAction.Save action, IDispatcher dispatcher)
    {
        var success = WriteSettingsConfig(action.Settings);

        if (!success) { return; }

        _preferencesProvider.DisabledDatabasesPreference = action.Settings.DisabledDatabases;

        _preferencesProvider.PrereleasePreference = action.Settings.IsPrereleaseEnabled;

        dispatcher.Dispatch(new SettingsAction.SaveCompleted(action.Settings));
    }

    private SettingsModel? ReadSettingsConfig()
    {
        try
        {
            ConfigSrwLock.EnterReadLock();

            using FileStream stream = File.OpenRead(_fileLocationOptions.SettingsPath);
            return JsonSerializer.Deserialize<SettingsModel>(stream);
        }
        catch
        { // File may not exist, can be ignored
            return null;
        }
        finally { ConfigSrwLock.ExitReadLock(); }
    }

    private bool WriteSettingsConfig(SettingsModel settings)
    {
        try
        {
            ConfigSrwLock.EnterWriteLock();

            var config = JsonSerializer.Serialize(settings);
            File.WriteAllText(_fileLocationOptions.SettingsPath, config);

            return true;
        }
        catch (Exception ex)
        {
            _traceLogger.Trace($"{nameof(SettingsEffects)}.{nameof(WriteSettingsConfig)} failed: {ex}");
            _alertDialogService.ShowAlert("Failed to save config",
                    $"An error occured while trying to save the configuration, please try again\r\n{ex.Message}",
                    "Ok");

            return false;
        }
        finally { ConfigSrwLock.ExitWriteLock(); }
    }
}

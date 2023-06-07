// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using Fluxor;
using System.Text.Json;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Store.Settings;

public class SettingsEffects
{
    private readonly ITraceLogger _traceLogger;
    private readonly FileLocationOptions _fileLocationOptions;
    private const string DisabledDatabasesPreference = "disabled-databases";
    private const string PrereleasePreference = "prerelease-enabled";
    private static readonly ReaderWriterLockSlim ConfigSrwLock = new();

    public SettingsEffects(ITraceLogger traceLogger, FileLocationOptions fileLocationOptions)
    {
        _traceLogger = traceLogger;
        _fileLocationOptions = fileLocationOptions;
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

        var disabledDatabases =
            JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(DisabledDatabasesPreference, "[]"));

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

        var disabledDatabases =
            JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(DisabledDatabasesPreference, "[]"));

        if (disabledDatabases?.Any() is true)
        {
            config.DisabledDatabases = disabledDatabases;
        }

        config.IsPrereleaseEnabled = Preferences.Default.Get(PrereleasePreference, false);

        dispatcher.Dispatch(new SettingsAction.LoadSettingsCompleted(config));
    }

    [EffectMethod]
    public async Task HandleSave(SettingsAction.Save action, IDispatcher dispatcher)
    {
        var success = WriteSettingsConfig(action.Settings);

        if (!success) { return; }

        var disabledDatabases = JsonSerializer.Serialize(action.Settings.DisabledDatabases);
        Preferences.Default.Set(DisabledDatabasesPreference, disabledDatabases);

        Preferences.Default.Set(PrereleasePreference, action.Settings.IsPrereleaseEnabled);

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
            if (Application.Current?.MainPage is not null)
            {
                Application.Current.MainPage.DisplayAlert("Failed to save config",
                    $"An error occured while trying to save the configuration, please try again\r\n{ex.Message}",
                    "Ok");
            }

            return false;
        }
        finally { ConfigSrwLock.ExitWriteLock(); }
    }
}

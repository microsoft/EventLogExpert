// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.Store.Settings;

public class SettingsReducer
{
    private static readonly ReaderWriterLockSlim ConfigSrwLock = new();

    [ReducerMethod(typeof(SettingsAction.LoadDatabases))]
    public static SettingsState ReduceLoadDatabases(SettingsState state)
    {
        List<string> databases = new();

        try
        {
            if (Directory.Exists(Utils.DatabasePath))
            {
                foreach (var item in Directory.EnumerateFiles(Utils.DatabasePath, "*.db"))
                {
                    databases.Add(Path.GetFileName(item));
                }
            }
        }
        catch
        { // TODO: Log Failure
            return state;
        }

        if (databases.Count <= 0) { return state; }

        SettingsModel? config = ReadSettingsConfig();

        if (config?.DisabledDatabases is not null)
        {
            databases.RemoveAll(enabled => config.DisabledDatabases
                .Any(disabled => string.Equals(enabled, disabled, StringComparison.InvariantCultureIgnoreCase)));
        }

        return state with { LoadedDatabases = SortDatabases(databases).ToImmutableList() };
    }

    [ReducerMethod(typeof(SettingsAction.LoadSettings))]
    public static SettingsState ReduceLoadSettings(SettingsState state)
    {
        SettingsModel? config = ReadSettingsConfig();

        if (config is null || string.IsNullOrEmpty(config.TimeZoneId)) { return state; }

        return state with { Config = config };
    }

    [ReducerMethod]
    public static SettingsState ReduceSave(SettingsState state, SettingsAction.Save action)
    {
        var success = WriteSettingsConfig(action.Settings);

        if (!success) { return state; }

        return state with { Config = action.Settings };
    }

    [ReducerMethod]
    public static SettingsState ReduceToggleShowComputerName(SettingsState state,
        SettingsAction.ToggleShowComputerName action) => state with { ShowComputerName = !state.ShowComputerName };

    [ReducerMethod]
    public static SettingsState ReduceToggleShowLogName(SettingsState state, SettingsAction.ToggleShowLogName action) =>
        state with { ShowLogName = !state.ShowLogName };

    private static SettingsModel? ReadSettingsConfig()
    {
        try
        {
            ConfigSrwLock.EnterReadLock();

            using FileStream stream = File.OpenRead(Utils.SettingsPath);
            return JsonSerializer.Deserialize<SettingsModel>(stream);
        }
        catch
        { // File may not exist, can be ignored
            return null;
        }
        finally { ConfigSrwLock.ExitReadLock(); }
    }

    private static IEnumerable<string> SortDatabases(IEnumerable<string> databases)
    {
        var r = new Regex("^(.+) (\\S+)$");

        return databases
            .Select(name =>
            {
                var m = r.Match(name);

                return m.Success
                    ? new { FirstPart = m.Groups[1].Value + " ", SecondPart = m.Groups[2].Value }
                    : new { FirstPart = name, SecondPart = "" };
            })
            .OrderBy(n => n.FirstPart)
            .ThenByDescending(n => n.SecondPart)
            .Select(n => n.FirstPart + n.SecondPart)
            .ToList();
    }

    private static bool WriteSettingsConfig(SettingsModel settings)
    {
        try
        {
            ConfigSrwLock.EnterWriteLock();

            var config = JsonSerializer.Serialize(settings);
            File.WriteAllText(Utils.SettingsPath, config);

            return true;
        }
        catch (Exception ex)
        { // TODO: Log a warning
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

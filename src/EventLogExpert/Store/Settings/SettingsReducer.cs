// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EventLogExpert.Store.Settings;

public class SettingsReducer
{
    [ReducerMethod]
    public static SettingsState ReduceLoadDatabasesCompleted(SettingsState state, SettingsAction.LoadDatabasesCompleted action)
    {
        return state with { LoadedDatabases = SortDatabases(action.loadedDatabases).ToImmutableList() };
    }

    [ReducerMethod]
    public static SettingsState ReduceLoadSettings(SettingsState state, SettingsAction.LoadSettingsCompleted action)
    {
        return state with { Config = action.config };
    }

    [ReducerMethod]
    public static SettingsState ReduceSave(SettingsState state, SettingsAction.SaveCompleted action)
    {
        return state with { Config = action.Settings };
    }

    [ReducerMethod]
    public static SettingsState ReduceToggleShowComputerName(SettingsState state,
        SettingsAction.ToggleShowComputerName action) => state with { ShowComputerName = !state.ShowComputerName };

    [ReducerMethod]
    public static SettingsState ReduceToggleShowLogName(SettingsState state, SettingsAction.ToggleShowLogName action) =>
        state with { ShowLogName = !state.ShowLogName };

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
}

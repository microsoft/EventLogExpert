// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Store.Settings;

public class SettingsReducer
{
    [ReducerMethod]
    public static SettingsState ReduceLoadDatabasesCompleted(SettingsState state,
        SettingsAction.LoadDatabasesCompleted action) => state with
    {
        LoadedDatabases = SortDatabases(action.LoadedDatabases).ToImmutableList()
    };

    [ReducerMethod]
    public static SettingsState ReduceLoadSettings(SettingsState state, SettingsAction.LoadSettingsCompleted action) =>
        state with { Config = action.Config };

    [ReducerMethod]
    public static SettingsState ReduceSave(SettingsState state, SettingsAction.SaveCompleted action) =>
        state with { Config = action.Settings };

    [ReducerMethod(typeof(SettingsAction.ToggleShowActivityId))]
    public static SettingsState ReduceToggleShowActivityId(SettingsState state) =>
        state with { ShowActivityId = !state.ShowActivityId };

    [ReducerMethod(typeof(SettingsAction.ToggleShowComputerName))]
    public static SettingsState ReduceToggleShowComputerName(SettingsState state) =>
        state with { ShowComputerName = !state.ShowComputerName };

    [ReducerMethod(typeof(SettingsAction.ToggleShowLogName))]
    public static SettingsState ReduceToggleShowLogName(SettingsState state) =>
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

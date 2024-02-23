// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Store.Settings;

public sealed partial class SettingsReducer
{
    [ReducerMethod]
    public static SettingsState ReduceLoadDatabasesCompleted(SettingsState state,
        SettingsAction.LoadDatabasesCompleted action) => state with
    {
        LoadedDatabases = SortDatabases(action.LoadedDatabases).ToImmutableList()
    };

    [ReducerMethod]
    public static SettingsState ReduceLoadSettings(SettingsState state, SettingsAction.LoadSettingsCompleted action) =>
        state with
        {
            Config = action.Config,
            DisabledDatabases = action.DisabledDatabases.ToImmutableList()
        };

    [ReducerMethod]
    public static SettingsState ReduceSaveCompleted(SettingsState state, SettingsAction.SaveCompleted action) =>
        state with { Config = action.Settings };

    [ReducerMethod]
    public static SettingsState ReduceSaveDisabledDatabasesCompleted(
        SettingsState state,
        SettingsAction.SaveDisabledDatabasesCompleted action) =>
        state with { DisabledDatabases = action.Databases.ToImmutableList() };

    private static IEnumerable<string> SortDatabases(IEnumerable<string> databases)
    {
        var r = SplitFileName();

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
            .Select(n => n.FirstPart + n.SecondPart);
    }

    [GeneratedRegex("^(.+) (\\S+)$")]
    private static partial Regex SplitFileName();
}

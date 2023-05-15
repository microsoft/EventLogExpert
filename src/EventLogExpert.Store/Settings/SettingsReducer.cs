﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Text.Json;

namespace EventLogExpert.Store.Settings;

public class SettingsReducer
{
    [ReducerMethod]
    public static SettingsState ReduceLoadProvider(SettingsState state, SettingsAction.LoadProviders action)
    {
        IEnumerable<string> providers = Enumerable.Empty<string>();

        try
        {
            if (Directory.Exists(action.Path))
            {
                providers = Directory.EnumerateFiles(action.Path, "*.db").Select(Path.GetFileName).OfType<string>();
            }
        }
        catch
        { // Directory may not exist, can be ignored
        }

        return state with { LoadedProviders = providers };
    }

    [ReducerMethod]
    public static SettingsState ReduceLoadSettings(SettingsState state, SettingsAction.LoadSettings action)
    {
        SettingsModel? config = null;

        try
        {
            using FileStream stream = File.OpenRead(action.Path);
            config = JsonSerializer.Deserialize<SettingsModel>(stream);
        }
        catch
        { // File may not exist, can be ignored
        }

        if (config is null || string.IsNullOrEmpty(config.TimeZoneId)) { return state; }

        return state with
        {
            TimeZoneId = config.TimeZoneId, TimeZone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId)
        };
    }

    [ReducerMethod]
    public static SettingsState ReduceSave(SettingsState state, SettingsAction.Save action)
    {
        try
        {
            var config = JsonSerializer.Serialize(action.Settings);
            File.WriteAllText(action.Path, config);

            return state with
            {
                TimeZoneId = action.Settings.TimeZoneId,
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(action.Settings.TimeZoneId)
            };
        }
        catch
        { // TODO: Log a warning
            return state;
        }
    }
}
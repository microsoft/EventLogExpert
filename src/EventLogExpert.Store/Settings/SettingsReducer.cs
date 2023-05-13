// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Text.Json;

namespace EventLogExpert.Store.Settings;

public class SettingsReducer
{
    public static string ProviderPath => Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EventLogExpert",
        "Databases");

    public static string SettingsPath => Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EventLogExpert",
        "settings.json");

    [ReducerMethod(typeof(SettingsAction.Load))]
    public static SettingsState ReduceLoad(SettingsState state)
    {
        SettingsModel? config = null;
        IEnumerable<string> providers = Enumerable.Empty<string>();

        // TODO: Split these into their own actions

        try
        {
            using FileStream stream = File.OpenRead(SettingsPath);
            config = JsonSerializer.Deserialize<SettingsModel>(stream);
        }
        catch
        { // File may not exist, can be ignored
        }

        try
        {
            if (Directory.Exists(ProviderPath))
            {
                providers = Directory.EnumerateFiles(ProviderPath, "*.db").Select(Path.GetFileName).OfType<string>();
            }
        }
        catch
        { // Directory may not exist, can be ignored
        }

        return new SettingsState { TimeZone = config?.TimeZoneOffset ?? 0, LoadedProviders = providers };
    }

    [ReducerMethod]
    public static SettingsState ReduceSave(SettingsState state, SettingsAction.Save action)
    {
        try
        {
            var config = JsonSerializer.Serialize(action.Settings);
            File.WriteAllText(Path.Join(action.Path, "settings.json"), config);
            //File.WriteAllText(SettingsPath, config);
            //using FileStream stream = File.OpenWrite(SettingsPath);
            //using StreamWriter writer = new(stream);

            //writer.Write(config);

            return state with { TimeZone = action.Settings.TimeZoneOffset };
        }
        catch
        { // TODO: Log a warning
            return state;
        }
    }
}

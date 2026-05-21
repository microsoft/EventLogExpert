// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database;
using System.Text.Json;

namespace EventLogExpert.Adapters.Settings;

internal sealed class DatabasePreferencesAdapter : IDatabasePreferencesProvider
{
    private const string DisabledDatabases = "disabled-databases";

    public IEnumerable<string> DisabledDatabasesPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(DisabledDatabases, "[]")) ?? [];
        set => Preferences.Default.Set(DisabledDatabases, JsonSerializer.Serialize(value));
    }
}

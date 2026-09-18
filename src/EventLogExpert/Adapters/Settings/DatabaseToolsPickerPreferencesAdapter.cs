// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DatabaseTools;

namespace EventLogExpert.Adapters.Settings;

internal sealed class DatabaseToolsPickerPreferencesAdapter : IDatabaseToolsPickerPreferencesProvider
{
    private const string ExistingDatabaseLastDirectory = "dbtools-picker-lastdir-existingdatabase";
    private const string OutputLastDirectory = "dbtools-picker-lastdir-output";
    private const string SourceLastDirectory = "dbtools-picker-lastdir-source";

    public string? GetLastDirectory(DatabaseToolsPickRole role) =>
        Preferences.Default.Get<string?>(GetPreferenceKey(role), null);

    public void SetLastDirectory(DatabaseToolsPickRole role, string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        Preferences.Default.Set(GetPreferenceKey(role), directory);
    }

    private static string GetPreferenceKey(DatabaseToolsPickRole role) => role switch
    {
        DatabaseToolsPickRole.Source => SourceLastDirectory,
        DatabaseToolsPickRole.Output => OutputLastDirectory,
        DatabaseToolsPickRole.ExistingDatabase => ExistingDatabaseLastDirectory,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };
}

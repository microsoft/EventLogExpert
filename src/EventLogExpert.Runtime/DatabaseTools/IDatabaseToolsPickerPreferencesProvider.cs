// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DatabaseTools;

public interface IDatabaseToolsPickerPreferencesProvider
{
    string? GetLastDirectory(DatabaseToolsPickRole role);

    void SetLastDirectory(DatabaseToolsPickRole role, string directory);
}

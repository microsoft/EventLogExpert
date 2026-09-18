// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DatabaseTools;

public interface IDatabaseToolsPickerDirectory
{
    Task<string?> ResolveInitialDirectoryAsync(DatabaseToolsPickRole role);

    void RememberDirectory(DatabaseToolsPickRole role, string? pickedPath);
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DatabaseTools;

public interface IDatabaseToolsFallbackDirectoryProvider
{
    IReadOnlyList<string> GetFallbackDirectories(DatabaseToolsPickRole role);
}

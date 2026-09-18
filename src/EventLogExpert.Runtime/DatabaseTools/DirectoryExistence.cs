// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DatabaseTools;

public sealed class DirectoryExistence : IDirectoryExistence
{
    public Task<bool> ExistsAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Task.Run(() => Directory.Exists(path));
    }
}

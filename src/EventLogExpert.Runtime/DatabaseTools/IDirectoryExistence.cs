// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DatabaseTools;

public interface IDirectoryExistence
{
    Task<bool> ExistsAsync(string path);
}

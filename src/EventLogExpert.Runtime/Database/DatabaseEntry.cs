// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Schema;

namespace EventLogExpert.Runtime.Database;

public sealed record DatabaseEntry(
    string FileName,
    string FullPath,
    bool IsEnabled,
    DatabaseStatus Status,
    bool BackupExists = false)
{
    // Body property preserves existing positional construction while carrying classification OS stamps.
    public IReadOnlyList<ProviderDatabaseOsStamp> OsStamps { get; init; } = [];
}

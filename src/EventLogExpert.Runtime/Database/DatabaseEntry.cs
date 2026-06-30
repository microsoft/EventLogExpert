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
    /// <summary>
    ///     The distinct source-OS stamps found in this database's providers, read during classification for a Ready
    ///     database. Empty for a legacy/unstamped database, a non-Ready one, or when the read could not be performed.
    ///     Init-only (not a positional parameter) so existing positional constructions are unaffected.
    /// </summary>
    public IReadOnlyList<ProviderDatabaseOsStamp> OsStamps { get; init; } = [];
}

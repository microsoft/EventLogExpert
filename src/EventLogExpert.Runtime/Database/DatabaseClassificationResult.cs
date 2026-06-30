// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Schema;

namespace EventLogExpert.Runtime.Database;

/// <summary>
///     The outcome of classifying one database file: its schema-derived <see cref="Status" />, whether an upgrade
///     backup exists, and the distinct source-OS stamps read for a Ready database (empty otherwise). Carried from
///     <c>DatabaseClassificationService</c> to <see cref="DatabaseRegistry.ApplyClassificationResults" /> so status,
///     backup, and OS stamps are applied to the registry entry as one atomic update.
/// </summary>
public sealed record DatabaseClassificationResult(
    DatabaseStatus Status,
    bool BackupExists,
    IReadOnlyList<ProviderDatabaseOsStamp> OsStamps);

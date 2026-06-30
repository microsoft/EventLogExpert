// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Schema;

namespace EventLogExpert.Runtime.Database;

public sealed record DatabaseClassificationResult(
    DatabaseStatus Status,
    bool BackupExists,
    IReadOnlyList<ProviderDatabaseOsStamp> OsStamps);

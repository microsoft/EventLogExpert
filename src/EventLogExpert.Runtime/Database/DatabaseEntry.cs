// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database;

public sealed record DatabaseEntry(
    string FileName,
    string FullPath,
    bool IsEnabled,
    DatabaseStatus Status,
    bool BackupExists = false);

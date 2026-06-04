// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.MergeDatabase;

/// <summary>
///     Merges providers from a source into a target database. By default, providers that already exist in the target
///     are skipped; set <see cref="Overwrite" /> to replace existing target rows with source data.
/// </summary>
/// <param name="SourcePath">Source: .db, .evtx, or folder containing them.</param>
/// <param name="TargetDatabasePath">Target .db file. Must exist.</param>
/// <param name="Overwrite">When true, providers already in the target are overwritten with source data.</param>
public sealed record MergeDatabaseRequest(string SourcePath, string TargetDatabasePath, bool Overwrite);

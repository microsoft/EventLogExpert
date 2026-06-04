// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.DiffDatabase;

/// <summary>
///     Produces a new database containing providers from <see cref="SecondSourcePath" /> that are NOT in
///     <see cref="FirstSourcePath" />. Each source may be a .db, an .evtx, or a folder containing them.
/// </summary>
/// <param name="FirstSourcePath">First source to compare.</param>
/// <param name="SecondSourcePath">
///     Second source; providers from here not in the first are written to
///     <see cref="NewDatabasePath" />.
/// </param>
/// <param name="NewDatabasePath">New .db file path. Must not already exist; must have .db extension.</param>
public sealed record DiffDatabaseRequest(string FirstSourcePath, string SecondSourcePath, string NewDatabasePath);

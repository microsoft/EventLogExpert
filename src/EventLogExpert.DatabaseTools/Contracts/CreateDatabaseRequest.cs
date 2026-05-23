// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Contracts;

/// <summary>
///     Creates a new provider database from local providers or from a source. When <see cref="SourcePath" /> is null,
///     local providers on this machine are used (no fallback). When supplied, ONLY the source is used.
/// </summary>
/// <param name="TargetPath">Target .db file path. Must not already exist; must have .db extension.</param>
/// <param name="SourcePath">Optional source: .db, .evtx, or folder. Null = local providers.</param>
/// <param name="FilterRegex">Optional regex applied to provider names; null = no filter.</param>
/// <param name="SkipProvidersInFile">Optional source whose provider names are excluded from the new database.</param>
public sealed record CreateDatabaseRequest(string TargetPath, string? SourcePath, Regex? FilterRegex, string? SkipProvidersInFile);

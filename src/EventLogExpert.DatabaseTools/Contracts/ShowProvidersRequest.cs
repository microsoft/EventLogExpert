// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Contracts;

/// <summary>
///     Lists providers from a source. When <see cref="SourcePath" /> is null/empty, local providers on this machine
///     are listed. <see cref="FilterRegex" /> is null when no name filter is applied.
/// </summary>
/// <param name="SourcePath">Optional source: .db, .evtx, or folder containing them. Null = local providers.</param>
/// <param name="FilterRegex">Optional case-insensitive regex applied to provider names.</param>
public sealed record ShowProvidersRequest(string? SourcePath, Regex? FilterRegex);

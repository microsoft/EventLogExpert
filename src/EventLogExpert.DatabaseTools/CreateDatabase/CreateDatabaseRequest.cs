// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.CreateDatabase;

/// <summary>
///     Creates a new provider database from local providers, a file source, or an offline Windows image. When both
///     <see cref="SourcePath" /> and <see cref="OfflineImagePath" /> are null, local providers on this machine are used
///     (no fallback). When <see cref="SourcePath" /> is supplied, ONLY that source is used. When
///     <see cref="OfflineImagePath" /> is supplied, ONLY that image is used, fully offline; it is mutually exclusive with
///     <see cref="SourcePath" />.
/// </summary>
/// <param name="TargetPath">Target .db file path. Must not already exist; must have .db extension.</param>
/// <param name="SourcePath">
///     Optional source: .db, .evtx, or folder. Null = local providers (unless an offline image is
///     given).
/// </param>
/// <param name="FilterRegex">Optional regex applied to provider names; null = no filter.</param>
/// <param name="SkipProvidersInFile">Optional source whose provider names are excluded from the new database.</param>
/// <param name="OfflineImagePath">
///     Optional offline Windows image to extract providers from, fully offline (no host
///     registry or host files). Null = not an offline build. Mutually exclusive with <paramref name="SourcePath" />.
/// </param>
/// <param name="ImageKind">
///     How <paramref name="OfflineImagePath" /> is accessed: a mounted volume/extracted folder (
///     <see cref="OfflineImageKind.Directory" />) or a <c>.wim</c>/<c>.esd</c> file (<see cref="OfflineImageKind.Wim" />,
///     which extracts <paramref name="WimIndex" /> first).
/// </param>
/// <param name="WimIndex">
///     The 1-based image index to extract from a <c>.wim</c>/<c>.esd</c>, for
///     <see cref="OfflineImageKind.Wim" />. Null otherwise.
/// </param>
public sealed record CreateDatabaseRequest(
    string TargetPath,
    string? SourcePath,
    Regex? FilterRegex,
    string? SkipProvidersInFile,
    string? OfflineImagePath = null,
    OfflineImageKind ImageKind = OfflineImageKind.Directory,
    int? WimIndex = null);

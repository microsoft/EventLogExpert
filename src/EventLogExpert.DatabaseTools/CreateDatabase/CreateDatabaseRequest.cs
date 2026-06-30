// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.CreateDatabase;

public sealed record CreateDatabaseRequest(
    string TargetPath,
    string? SourcePath,
    Regex? FilterRegex,
    string? SkipProvidersInFile,
    string? OfflineImagePath = null,
    OfflineImageKind? ImageKind = null,
    int? WimIndex = null,
    bool Overwrite = false);

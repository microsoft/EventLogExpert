// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database;

public sealed record ImportResult(
    int Imported,
    IReadOnlyList<string> ImportedNames,
    IReadOnlyList<ImportFailure> Failures,
    IReadOnlyList<ImportFailure> UpgradeFailures);

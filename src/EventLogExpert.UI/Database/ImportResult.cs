// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database;

public sealed record ImportResult(
    int Imported,
    IReadOnlyList<ImportFailure> Failures,
    IReadOnlyList<ImportFailure> UpgradeFailures);

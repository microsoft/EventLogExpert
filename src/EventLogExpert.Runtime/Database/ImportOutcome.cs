// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database;

public readonly record struct ImportOutcome(
    int ImportedCount,
    IReadOnlyList<ImportFailure> Failures,
    IReadOnlyList<ImportFailure> UpgradeFailures)
{
    public static ImportOutcome None { get; } = new(0, [], []);

    public bool DatabaseStateChanged => ImportedCount > 0;
}

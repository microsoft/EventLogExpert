// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class NoOpBackslashNameMigrator : IBackslashNameMigrator
{
    public BackslashMigrationResult BuildMigrationPlan(IReadOnlyList<LibraryEntry> currentEntries) =>
        new([], 0);

    public void MarkMigrationCompleted() { }

    public bool ShouldRunMigration() => false;
}

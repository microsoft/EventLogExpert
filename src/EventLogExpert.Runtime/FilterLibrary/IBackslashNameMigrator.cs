// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal interface IBackslashNameMigrator
{
    BackslashMigrationResult BuildMigrationPlan(IReadOnlyList<LibraryEntry> currentEntries);

    void MarkMigrationCompleted();

    bool ShouldRunMigration();
}

internal sealed record BackslashMigrationResult(
    ImmutableList<LibraryEntry> UpdatedEntries,
    int CollisionsDisambiguated);

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class NoOpLegacyFilterMigrator : ILegacyFilterMigrator
{
    public LegacyMigrationResult BuildEntriesFromLegacy() =>
        new(ImmutableList<LibraryEntry>.Empty, LegacyMigrationSections.None);

    public void DeleteLegacyData(LegacyMigrationSections sectionsToDelete) { }

    public void MarkMigrationCompleted(LegacyMigrationSections successfulSections) { }

    public bool ShouldRunMigration() => false;
}

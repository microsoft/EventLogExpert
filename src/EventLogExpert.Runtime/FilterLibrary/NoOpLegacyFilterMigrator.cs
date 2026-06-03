// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

/// <summary>
///     Inert <see cref="ILegacyFilterMigrator" /> registered when <see cref="LegacyMigrationFeature.Enabled" /> is
///     <see langword="false" />. Satisfies the <see cref="Effects" /> ctor dependency without reading or mutating any
///     preferences, letting the migrator path remain present in the DI graph for a future mechanical removal pass.
/// </summary>
internal sealed class NoOpLegacyFilterMigrator : ILegacyFilterMigrator
{
    public LegacyMigrationResult BuildEntriesFromLegacy() =>
        new(ImmutableList<LibraryEntry>.Empty, LegacyMigrationSections.None);

    public void DeleteLegacyData(LegacyMigrationSections sectionsToDelete) { }

    public void MarkMigrationCompleted(LegacyMigrationSections successfulSections) { }

    public bool ShouldRunMigration() => false;
}

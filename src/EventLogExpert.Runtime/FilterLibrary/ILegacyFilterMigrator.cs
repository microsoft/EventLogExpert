// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

public interface ILegacyFilterMigrator
{
    /// <summary>
    ///     Builds library entries from legacy preference keys. Each section (favorites, groups) is built independently;
    ///     per-item exceptions discard the whole section and leave its flag UNSET. ABSENT keys are treated as
    ///     parsed-successfully and DO set the section flag. Recents flag is always set.
    /// </summary>
    LegacyMigrationResult BuildEntriesFromLegacy();

    /// <summary>
    ///     Removes legacy preference keys corresponding to <paramref name="sectionsToDelete" />. NOT called by the
    ///     migration adapter itself; reserved for a future cleanup pass after migration is confirmed stable.
    /// </summary>
    void DeleteLegacyData(LegacyMigrationSections sectionsToDelete);

    /// <summary>Persists <paramref name="successfulSections" /> as the migration completion bitmask.</summary>
    void MarkMigrationCompleted(LegacyMigrationSections successfulSections);

    /// <summary>Returns <c>true</c> unless the persisted bitmask has both Favorites and Groups flags set.</summary>
    bool ShouldRunMigration();
}

public sealed record LegacyMigrationResult(
    ImmutableList<LibraryEntry> Entries,
    LegacyMigrationSections SuccessfulSections);

[Flags]
public enum LegacyMigrationSections
{
    None = 0,
    Favorites = 1,
    Groups = 2,

    /// <summary>Always set (recents are dropped, not migrated). Used by future cleanup paths to drive recents-key deletion.</summary>
    Recents = 4,
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class LegacyFilterMigrator(ILegacyPreferences preferences, ITraceLogger logger) : ILegacyFilterMigrator
{
    private const int DisplayNameMaxLength = 80;
    private const string FavoriteFiltersKey = "favorite-filters";
    private const string MigrationSectionsKey = "filter-library-migration-sections";
    private const string RecentFiltersKey = "recent-filters";
    private const LegacyMigrationSections RequiredForCompletion =
        LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups;
    private const string SavedGroupsKey = "saved-filters";

    public LegacyMigrationResult BuildEntriesFromLegacy()
    {
        var alreadyCompleted = ReadCompletedSections();
        var builder = ImmutableList.CreateBuilder<LibraryEntry>();
        var now = DateTimeOffset.UtcNow;

        // Carry forward the persisted bitmask so retries preserve sections completed by prior launches.
        // Recents is always set (recents are dropped, not migrated).
        var successful = alreadyCompleted | LegacyMigrationSections.Recents;

        if (!alreadyCompleted.HasFlag(LegacyMigrationSections.Favorites) && TryReadFavorites(out var favorites))
        {
            var localFavoriteEntries = new List<LibraryEntry>();
            bool favoritesSucceeded = true;

            try
            {
                foreach (var favText in favorites.Where(static s => !string.IsNullOrWhiteSpace(s)))
                {
                    var filter = SavedFilter.LoadFromPersisted(
                        favText,
                        HighlightColor.None,
                        isExcluded: false,
                        persistedBasicFilter: null,
                        mode: FilterMode.Advanced);

                    localFavoriteEntries.Add(new LibraryEntrySavedFilter
                    {
                        Id = LibraryEntryId.Create(),
                        Name = TruncateForDisplay(favText),
                        CreatedUtc = now,
                        Filter = filter with { IsEnabled = false },
                        IsFavorite = true,
                        LastUsedUtc = null,
                        Origin = LibraryEntryOrigin.UserSaved,
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"Failed to migrate favorite filters (partial section discarded): {ex.Message}");
                favoritesSucceeded = false;
            }

            if (favoritesSucceeded)
            {
                if (BackslashMigrationFeature.IsEnabled)
                {
                    builder.AddRange(localFavoriteEntries.Select(LibraryEntryTagNormalizer.MigrateBackslashName));
                }
                else
                {
                    builder.AddRange(localFavoriteEntries);
                }

                successful |= LegacyMigrationSections.Favorites;
            }
        }

        if (alreadyCompleted.HasFlag(LegacyMigrationSections.Groups) || !TryReadGroups(out var groups))
        {
            return new LegacyMigrationResult(builder.ToImmutable(), successful);
        }

        var localGroupEntries = new List<LibraryEntry>();
        bool groupsSucceeded = true;

        try
        {
            foreach (var group in groups.Where(static g => g.Filters.Count > 0))
            {
                localGroupEntries.Add(new LibraryEntryFilterSet
                {
                    Id = LibraryEntryId.Create(),
                    Name = string.IsNullOrWhiteSpace(group.Name) ? "(unnamed)" : group.Name,
                    CreatedUtc = now,
                    Filters = [.. group.Filters.Select(f => f with { Id = FilterId.Create(), IsEnabled = false })],
                    IsFavorite = false,
                    LastUsedUtc = null,
                    Origin = LibraryEntryOrigin.UserSaved,
                });
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to migrate filter groups (partial section discarded): {ex.Message}");
            groupsSucceeded = false;
        }

        if (!groupsSucceeded)
        {
            return new LegacyMigrationResult(builder.ToImmutable(), successful);
        }

        if (BackslashMigrationFeature.IsEnabled)
        {
            builder.AddRange(localGroupEntries.Select(LibraryEntryTagNormalizer.MigrateBackslashName));
        }
        else
        {
            var rejected = localGroupEntries.Where(e => e.Name.Contains('\\')).ToList();

            if (rejected.Count > 0)
            {
                logger.Warning($"Skipped {rejected.Count} legacy filter groups with invalid names containing '\\'.");
            }

            builder.AddRange(localGroupEntries.Where(e => !e.Name.Contains('\\')));
        }

        successful |= LegacyMigrationSections.Groups;

        return new LegacyMigrationResult(builder.ToImmutable(), successful);
    }

    public void DeleteLegacyData(LegacyMigrationSections sectionsToDelete)
    {
        if (sectionsToDelete.HasFlag(LegacyMigrationSections.Favorites))
        {
            preferences.Remove(FavoriteFiltersKey);
        }

        if (sectionsToDelete.HasFlag(LegacyMigrationSections.Groups))
        {
            preferences.Remove(SavedGroupsKey);
        }

        if (sectionsToDelete.HasFlag(LegacyMigrationSections.Recents))
        {
            preferences.Remove(RecentFiltersKey);
        }
    }

    public void MarkMigrationCompleted(LegacyMigrationSections successfulSections) =>
        preferences.SetString(
            MigrationSectionsKey,
            ((int)successfulSections).ToString(CultureInfo.InvariantCulture));

    public bool ShouldRunMigration() =>
        (ReadCompletedSections() & RequiredForCompletion) != RequiredForCompletion;

    private static string TruncateForDisplay(string text) =>
        text.Length <= DisplayNameMaxLength ? text : text[..(DisplayNameMaxLength - 3)] + "...";

    private LegacyMigrationSections ReadCompletedSections()
    {
        var raw = preferences.GetString(MigrationSectionsKey);

        if (raw is null ||
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectionsInt) ||
            sectionsInt < 0)
        {
            return LegacyMigrationSections.None;
        }

        return (LegacyMigrationSections)sectionsInt;
    }

    private bool TryReadFavorites(out List<string> favorites)
    {
        favorites = [];

        if (!preferences.ContainsKey(FavoriteFiltersKey)) { return true; }

        try
        {
            var json = preferences.GetString(FavoriteFiltersKey) ?? "[]";
            favorites = JsonSerializer.Deserialize<List<string>>(json) ?? [];

            return true;
        }
        catch (JsonException ex)
        {
            logger.Warning($"Failed to parse legacy favorite-filters preference: {ex.Message}");

            return false;
        }
    }

    private bool TryReadGroups(out List<SavedFilterGroup> groups)
    {
        groups = [];

        if (!preferences.ContainsKey(SavedGroupsKey)) { return true; }

        try
        {
            var json = preferences.GetString(SavedGroupsKey) ?? "[]";
            groups = JsonSerializer.Deserialize<List<SavedFilterGroup>>(json) ?? [];

            return true;
        }
        catch (JsonException ex)
        {
            logger.Warning($"Failed to parse legacy saved-filters preference: {ex.Message}");

            return false;
        }
    }
}

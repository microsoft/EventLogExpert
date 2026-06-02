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
        var builder = ImmutableList.CreateBuilder<LibraryEntry>();
        var now = DateTimeOffset.UtcNow;
        var successful = LegacyMigrationSections.Recents;

        if (TryReadFavorites(out var favorites))
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
                builder.AddRange(localFavoriteEntries);
                successful |= LegacyMigrationSections.Favorites;
            }
        }

        if (TryReadGroups(out var groups))
        {
            var localGroupEntries = new List<LibraryEntry>();
            bool groupsSucceeded = true;

            try
            {
                foreach (var group in groups.Where(static g => g.Filters.Count > 0))
                {
                    localGroupEntries.Add(new LibraryEntryPreset
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

            if (groupsSucceeded)
            {
                builder.AddRange(localGroupEntries);
                successful |= LegacyMigrationSections.Groups;
            }
        }

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

    public bool ShouldRunMigration()
    {
        var raw = preferences.GetString(MigrationSectionsKey);

        if (raw is null ||
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectionsInt) ||
            sectionsInt < 0)
        {
            return true;
        }

        return ((LegacyMigrationSections)sectionsInt & RequiredForCompletion) != RequiredForCompletion;
    }

    private static string TruncateForDisplay(string text) =>
        text.Length <= DisplayNameMaxLength ? text : text[..(DisplayNameMaxLength - 3)] + "...";

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

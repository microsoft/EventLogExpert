// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using NSubstitute;
using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class LegacyFilterMigratorTests
{
    private const string FavoriteFiltersKey = "favorite-filters";
    private const string RecentFiltersKey = "recent-filters";
    private const string SavedGroupsKey = "saved-filters";

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesAbsent_GroupsValid_BothRequiredSectionsSuccessful()
    {
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new() { Name = "G1", Filters = [SavedFilter.TryCreate("Level == 4")!] },
        });
        var prefs = StubPreferences(groupsJson: groupsJson);
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.IsType<LibraryEntryFilterSet>(Assert.Single(result.Entries));
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Favorites));
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Groups));
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesAlreadyCompleted_SkipsFavoritesBuildEvenWhenLegacyKeyPresent()
    {
        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 4", "Level == 5" });
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new() { Name = "G1", Filters = [SavedFilter.TryCreate("Level == 4")!] },
        });
        var prefs = StubPreferences(favoritesJson: favoritesJson, groupsJson: groupsJson);
        prefs.GetString("filter-library-migration-sections")
            .Returns(((int)(LegacyMigrationSections.Favorites | LegacyMigrationSections.Recents)).ToString());
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        // Only the group entry is built — favorites are skipped because their bit is already set.
        Assert.IsType<LibraryEntryFilterSet>(Assert.Single(result.Entries));
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Favorites));
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Groups));
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesContainsWhitespaceEntries_WhitespaceIsFilteredOut()
    {
        var favoritesJson = JsonSerializer.Serialize(new List<string> { "", "  ", "Level == 4", "\t" });
        var prefs = StubPreferences(favoritesJson: favoritesJson);
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Single(result.Entries);
        var entry = Assert.IsType<LibraryEntrySavedFilter>(result.Entries[0]);
        Assert.Equal("Level == 4", entry.Filter.ComparisonText);
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesCorrupt_GroupsAbsent_GroupsAndRecentsOnly_FavoritesUnset()
    {
        var prefs = StubPreferences(favoritesJson: "{not-json");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Empty(result.Entries);
        Assert.False(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Favorites));
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Groups));
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Recents));
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesCorrupt_GroupsValid_MigratesGroupsOnly_FavoritesFlagUnset()
    {
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new()
            {
                Name = "G1",
                Filters = [SavedFilter.TryCreate("Level == 4")!],
            },
        });
        var prefs = StubPreferences(favoritesJson: "{not-json", groupsJson: groupsJson);
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Single(result.Entries);
        Assert.IsType<LibraryEntryFilterSet>(result.Entries[0]);
        Assert.Equal(
            LegacyMigrationSections.Groups | LegacyMigrationSections.Recents,
            result.SuccessfulSections);
        Assert.False(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Favorites));
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesCorruptJson_GroupsCorruptJson_ReturnsEmpty_OnlyRecentsSuccessful()
    {
        var prefs = StubPreferences(favoritesJson: "{not-json", groupsJson: "[\"unterminated");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Empty(result.Entries);
        Assert.Equal(LegacyMigrationSections.Recents, result.SuccessfulSections);
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesEmpty_GroupsCorrupt_FavoritesSuccessful_NoEntries()
    {
        var prefs = StubPreferences(favoritesJson: "[]", groupsJson: "{not-json");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Empty(result.Entries);
        Assert.Equal(
            LegacyMigrationSections.Favorites | LegacyMigrationSections.Recents,
            result.SuccessfulSections);
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesEmptyArray_GroupsEmptyArray_AllSectionsSuccessful_NoEntries()
    {
        var prefs = StubPreferences(favoritesJson: "[]", groupsJson: "[]");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Empty(result.Entries);
        Assert.Equal(
            LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents,
            result.SuccessfulSections);
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesValid_AssertsCanonicalEntryShape()
    {
        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 4" });
        var prefs = StubPreferences(favoritesJson: favoritesJson);
        var before = DateTimeOffset.UtcNow;

        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var result = sut.BuildEntriesFromLegacy();
        var after = DateTimeOffset.UtcNow;

        var entry = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result.Entries));
        Assert.True(entry.IsFavorite);
        Assert.Equal(LibraryEntryOrigin.UserSaved, entry.Origin);
        Assert.Null(entry.LastUsedUtc);
        Assert.False(entry.Filter.IsEnabled);
        Assert.InRange(entry.CreatedUtc, before, after);
        Assert.NotEqual(default, entry.Id);
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesValid_GroupsCorrupt_MigratesFavoritesOnly_GroupsFlagUnset()
    {
        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 2" });
        var prefs = StubPreferences(favoritesJson: favoritesJson, groupsJson: "{not-json");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Single(result.Entries);
        Assert.IsType<LibraryEntrySavedFilter>(result.Entries[0]);
        Assert.Equal(
            LegacyMigrationSections.Favorites | LegacyMigrationSections.Recents,
            result.SuccessfulSections);
        Assert.False(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Groups));
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesValidAndGroupsValid_ReturnsBothSetsAndAllSectionsSuccessful()
    {
        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 2", "Source == \"x\"" });
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new()
            {
                Name = "G1",
                Filters = [SavedFilter.TryCreate("Level == 4")!],
            },
        });
        var prefs = StubPreferences(favoritesJson: favoritesJson, groupsJson: groupsJson);
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(2, result.Entries.OfType<LibraryEntrySavedFilter>().Count());
        Assert.Single(result.Entries.OfType<LibraryEntryFilterSet>());
        Assert.Equal(
            LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents,
            result.SuccessfulSections);
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoritesWithUncompilableExpression_StillCreatesEntry()
    {
        var favoritesJson = JsonSerializer.Serialize(new List<string> { "??? not a valid filter ???" });
        var prefs = StubPreferences(favoritesJson: favoritesJson);

        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var result = sut.BuildEntriesFromLegacy();

        var entry = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result.Entries));
        Assert.Equal("??? not a valid filter ???", entry.Filter.ComparisonText);
        Assert.Null(entry.Filter.Compiled);
        Assert.False(entry.Filter.IsEnabled);
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Favorites));
    }

    [Fact]
    public void BuildEntriesFromLegacy_FavoriteTextLongerThanDisplayLimit_NameIsTruncatedWithEllipsis()
    {
        var longText = new string('a', 200);
        var favoritesJson = JsonSerializer.Serialize(new List<string> { longText });
        var prefs = StubPreferences(favoritesJson: favoritesJson);

        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var result = sut.BuildEntriesFromLegacy();

        var entry = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result.Entries));
        Assert.Equal(80, entry.Name.Length);
        Assert.EndsWith("...", entry.Name);
        Assert.Equal(longText, entry.Filter.ComparisonText);
    }

    [Fact]
    public void BuildEntriesFromLegacy_FeatureDisabled_FavoriteNameWithBackslash_IsPreservedAsIs()
    {
        var favoritesJson = JsonSerializer.Serialize(new List<string> { @"PathLike\Filter\Text == ""x""" });
        var prefs = StubPreferences(favoritesJson: favoritesJson);
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        using var _ = BackslashMigrationFeature.Override(false);

        var result = sut.BuildEntriesFromLegacy();

        var entry = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result.Entries));
        Assert.Contains('\\', entry.Name);
        Assert.Empty(entry.Tags);
    }

    [Fact]
    public void BuildEntriesFromLegacy_FeatureDisabled_GroupNameWithBackslash_IsRejectedWithWarning()
    {
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new() { Name = @"Exchange\HUB", Filters = [SavedFilter.TryCreate("Level == 4")!] },
            new() { Name = "CleanName", Filters = [SavedFilter.TryCreate("Level == 5")!] },
        });
        var prefs = StubPreferences(groupsJson: groupsJson);
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        using var _ = BackslashMigrationFeature.Override(false);

        var result = sut.BuildEntriesFromLegacy();

        var entry = Assert.IsType<LibraryEntryFilterSet>(Assert.Single(result.Entries));
        Assert.Equal("CleanName", entry.Name);
        Assert.Empty(entry.Tags);
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Groups));
    }

    [Fact]
    public void BuildEntriesFromLegacy_GroupNameWithBackslash_AutoMigratedToFlatNameAndTags()
    {
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new() { Name = @"Exchange\HUB", Filters = [SavedFilter.TryCreate("Level == 4")!] },
        });
        var prefs = StubPreferences(groupsJson: groupsJson);
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        var entry = Assert.IsType<LibraryEntryFilterSet>(Assert.Single(result.Entries));
        Assert.Equal("HUB", entry.Name);
        Assert.Equal(["exchange"], entry.Tags);
    }

    [Fact]
    public void BuildEntriesFromLegacy_GroupsAlreadyCompleted_SkipsGroupsBuildEvenWhenLegacyKeyPresent()
    {
        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 4" });
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new() { Name = "G1", Filters = [SavedFilter.TryCreate("Level == 4")!] },
        });
        var prefs = StubPreferences(favoritesJson: favoritesJson, groupsJson: groupsJson);
        prefs.GetString("filter-library-migration-sections")
            .Returns(((int)(LegacyMigrationSections.Groups | LegacyMigrationSections.Recents)).ToString());
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result.Entries));
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Favorites));
        Assert.True(result.SuccessfulSections.HasFlag(LegacyMigrationSections.Groups));
    }

    [Fact]
    public void BuildEntriesFromLegacy_GroupsContainsEmptyFilterLists_EmptyGroupsAreFilteredOut()
    {
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new() { Name = "EmptyOne", Filters = [] },
            new() { Name = "WithFilters", Filters = [SavedFilter.TryCreate("Level == 4")!] },
            new() { Name = "EmptyTwo", Filters = [] },
        });
        var prefs = StubPreferences(groupsJson: groupsJson);
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        var filterSet = Assert.IsType<LibraryEntryFilterSet>(Assert.Single(result.Entries));
        Assert.Equal("WithFilters", filterSet.Name);
    }

    [Fact]
    public void BuildEntriesFromLegacy_GroupsValid_FilterIdsAreRegenerated()
    {
        var originalFilter = SavedFilter.TryCreate("Level == 4")!;
        var originalFilterId = originalFilter.Id;
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new() { Name = "G1", Filters = [originalFilter] },
        });
        var prefs = StubPreferences(groupsJson: groupsJson);

        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var result = sut.BuildEntriesFromLegacy();

        var filterSet = Assert.IsType<LibraryEntryFilterSet>(Assert.Single(result.Entries));
        Assert.False(filterSet.IsFavorite);
        Assert.Equal(LibraryEntryOrigin.UserSaved, filterSet.Origin);
        Assert.Null(filterSet.LastUsedUtc);
        var migratedFilter = Assert.Single(filterSet.Filters);
        Assert.NotEqual(originalFilterId, migratedFilter.Id);
        Assert.False(migratedFilter.IsEnabled);
    }

    [Fact]
    public void BuildEntriesFromLegacy_NoLegacyKeys_ReturnsEmptyEntriesAndAllRequiredSectionsSuccessful()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        var result = sut.BuildEntriesFromLegacy();

        Assert.Empty(result.Entries);
        Assert.Equal(
            LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents,
            result.SuccessfulSections);
    }

    [Fact]
    public void DeleteLegacyData_AllFlags_RemovesAllThreeKeys()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        sut.DeleteLegacyData(
            LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents);

        prefs.Received(1).Remove(FavoriteFiltersKey);
        prefs.Received(1).Remove(SavedGroupsKey);
        prefs.Received(1).Remove(RecentFiltersKey);
    }

    [Fact]
    public void DeleteLegacyData_Favorites_RemovesOnlyFavoriteFiltersKey()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        sut.DeleteLegacyData(LegacyMigrationSections.Favorites);

        prefs.Received(1).Remove(FavoriteFiltersKey);
        prefs.DidNotReceive().Remove(SavedGroupsKey);
        prefs.DidNotReceive().Remove(RecentFiltersKey);
    }

    [Fact]
    public void DeleteLegacyData_Groups_RemovesOnlySavedGroupsKey()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        sut.DeleteLegacyData(LegacyMigrationSections.Groups);

        prefs.Received(1).Remove(SavedGroupsKey);
        prefs.DidNotReceive().Remove(FavoriteFiltersKey);
        prefs.DidNotReceive().Remove(RecentFiltersKey);
    }

    [Fact]
    public void DeleteLegacyData_None_RemovesNothing()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        sut.DeleteLegacyData(LegacyMigrationSections.None);

        prefs.DidNotReceive().Remove(Arg.Any<string>());
    }

    [Fact]
    public void DeleteLegacyData_Recents_RemovesOnlyRecentFiltersKey()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        sut.DeleteLegacyData(LegacyMigrationSections.Recents);

        prefs.Received(1).Remove(RecentFiltersKey);
        prefs.DidNotReceive().Remove(FavoriteFiltersKey);
        prefs.DidNotReceive().Remove(SavedGroupsKey);
    }

    [Fact]
    public void MarkMigrationCompleted_PartialBitmask_WritesPartialValue()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        sut.MarkMigrationCompleted(LegacyMigrationSections.Favorites | LegacyMigrationSections.Recents);

        prefs.Received(1).SetString("filter-library-migration-sections", "5");
    }

    [Fact]
    public void MarkMigrationCompleted_WritesBitmaskAsDecimalString()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        sut.MarkMigrationCompleted(
            LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents);

        prefs.Received(1).SetString("filter-library-migration-sections", "7");
    }

    [Fact]
    public void ShouldRunMigration_FlagBitmaskContainsBothFavoritesAndGroups_ReturnsFalse()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        prefs.GetString("filter-library-migration-sections").Returns("7");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        Assert.False(sut.ShouldRunMigration());
    }

    [Fact]
    public void ShouldRunMigration_FlagBitmaskMissingFavorites_ReturnsTrue()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        prefs.GetString("filter-library-migration-sections").Returns("6");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        Assert.True(sut.ShouldRunMigration());
    }

    [Fact]
    public void ShouldRunMigration_FlagBitmaskMissingGroups_ReturnsTrue()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        prefs.GetString("filter-library-migration-sections").Returns("5");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        Assert.True(sut.ShouldRunMigration());
    }

    [Fact]
    public void ShouldRunMigration_FlagKeyAbsent_ReturnsTrue()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        Assert.True(sut.ShouldRunMigration());
    }

    [Fact]
    public void ShouldRunMigration_FlagKeyContainsNegativeInteger_ReturnsTrue()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        prefs.GetString("filter-library-migration-sections").Returns("-1");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        Assert.True(sut.ShouldRunMigration());
    }

    [Fact]
    public void ShouldRunMigration_FlagKeyContainsNonInteger_ReturnsTrue()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        prefs.GetString("filter-library-migration-sections").Returns("hello");
        var sut = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());

        Assert.True(sut.ShouldRunMigration());
    }

    private static ILegacyPreferences StubPreferences(string? favoritesJson = null, string? groupsJson = null)
    {
        var prefs = Substitute.For<ILegacyPreferences>();

        if (favoritesJson is not null)
        {
            prefs.GetString(FavoriteFiltersKey).Returns(favoritesJson);
            prefs.ContainsKey(FavoriteFiltersKey).Returns(true);
        }

        if (groupsJson is not null)
        {
            prefs.GetString(SavedGroupsKey).Returns(groupsJson);
            prefs.ContainsKey(SavedGroupsKey).Returns(true);
        }

        return prefs;
    }
}

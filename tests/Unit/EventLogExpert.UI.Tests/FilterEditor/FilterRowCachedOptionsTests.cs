// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.FilterEditor;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using System.Reflection;

namespace EventLogExpert.UI.Tests.FilterEditor;

/// <summary>
///     Pure-instance unit tests for <see cref="FilterRow.CachedOptions" /> (Δ30 contract): explicit
///     <c>OfType&lt;LibraryEntrySavedFilter&gt;</c> filtering, favorites-first ordered alphabetically (OrdinalIgnoreCase),
///     then non-favorite recents ordered by <c>LastUsedUtc</c> descending, with cross-bucket dedupe by
///     <c>ComparisonText</c> only (legacy quick-pick contract).
/// </summary>
public sealed class FilterRowCachedOptionsTests
{
    [Fact]
    public void CachedOptions_FavoriteAndIdenticalRecent_DedupesAndFavoriteWins()
    {
        var fav = BuildSavedFilter("MyFav", filterText: "Level == 4", isFavorite: true);
        var recent = BuildSavedFilter(
            "DuplicateName",
            filterText: "Level == 4",
            isFavorite: false,
            lastUsed: DateTimeOffset.UtcNow);
        var row = NewRowWithLibraryEntries(fav, recent);

        var options = row.CachedOptions;

        Assert.Single(options);
        Assert.True(options[0].IsFavorite);
        Assert.Equal("Level == 4", options[0].Value);
    }

    [Fact]
    public void CachedOptions_LibraryEntryFilterSet_IsExcluded()
    {
        var filterSet = new LibraryEntryFilterSet
        {
            Name = "MyPreset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = ImmutableList<SavedFilter>.Empty,
        };
        var fav = BuildSavedFilter("Fav", filterText: "Level == 4", isFavorite: true);
        var row = NewRowWithLibraryEntries(filterSet, fav);

        var options = row.CachedOptions;

        Assert.Single(options);
        Assert.Equal("Level == 4", options[0].Value);
    }

    [Fact]
    public void CachedOptions_NonFavoriteWithoutLastUsed_IsExcluded()
    {
        var orphan = BuildSavedFilter("Orphan", filterText: "Level == 4", isFavorite: false, lastUsed: null);
        var row = NewRowWithLibraryEntries(orphan);

        var options = row.CachedOptions;

        Assert.Empty(options);
    }

    [Fact]
    public void CachedOptions_Recomputes_WhenLibraryEntriesReferenceChanges()
    {
        var stateMock = Substitute.For<IState<FilterLibraryState>>();
        stateMock.Value.Returns(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.CreateRange(
                new LibraryEntry[] { BuildSavedFilter("Fav", filterText: "Level == 4", isFavorite: true) }),
        });
        var row = NewRowWithState(stateMock);

        var first = row.CachedOptions;
        Assert.Single(first);

        stateMock.Value.Returns(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.CreateRange(new LibraryEntry[]
            {
                BuildSavedFilter("Fav", filterText: "Level == 4", isFavorite: true),
                BuildSavedFilter("Fav2", filterText: "Level == 2", isFavorite: true),
            }),
        });

        var second = row.CachedOptions;

        Assert.NotSame(first, second);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void CachedOptions_ReturnsStableReference_WhenLibraryStateUnchanged()
    {
        var row = NewRowWithLibraryEntries(BuildSavedFilter("Fav", filterText: "Level == 4", isFavorite: true));

        var first = row.CachedOptions;
        var second = row.CachedOptions;

        Assert.Same(first, second);
    }

    [Fact]
    public void CachedOptions_SameTextDifferentTags_UnionsTagsIntoOneOption()
    {
        var favNetwork = BuildSavedFilter("Fav", filterText: "Level == 4", isFavorite: true, tags: ["network"]);
        var recentSecurity = BuildSavedFilter(
            "Recent",
            filterText: "Level == 4",
            isFavorite: false,
            lastUsed: DateTimeOffset.UtcNow,
            tags: ["security"]);
        var row = NewRowWithLibraryEntries(favNetwork, recentSecurity);

        var options = row.CachedOptions;

        Assert.Single(options);
        Assert.True(options[0].IsFavorite);
        Assert.Equal(
            new[] { "network", "security" },
            options[0].Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void CachedOptions_TwoFavorites_OrderedByNameOrdinalIgnoreCase()
    {
        var zebra = BuildSavedFilter("zebra", filterText: "Level == 1", isFavorite: true);
        var alpha = BuildSavedFilter("Alpha", filterText: "Level == 2", isFavorite: true);
        var row = NewRowWithLibraryEntries(zebra, alpha);

        var options = row.CachedOptions;

        Assert.Equal(2, options.Count);
        Assert.Equal("Level == 2", options[0].Value);
        Assert.Equal("Level == 1", options[1].Value);
    }

    [Fact]
    public void CachedOptions_TwoNonFavoriteRecents_OrderedByLastUsedDescending()
    {
        var older = BuildSavedFilter(
            "Older",
            filterText: "Level == 1",
            isFavorite: false,
            lastUsed: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = BuildSavedFilter(
            "Newer",
            filterText: "Level == 2",
            isFavorite: false,
            lastUsed: DateTimeOffset.UtcNow);
        var row = NewRowWithLibraryEntries(older, newer);

        var options = row.CachedOptions;

        Assert.Equal(2, options.Count);
        Assert.Equal("Level == 2", options[0].Value);
        Assert.False(options[0].IsFavorite);
        Assert.Equal("Level == 1", options[1].Value);
        Assert.False(options[1].IsFavorite);
    }

    private static LibraryEntrySavedFilter BuildSavedFilter(
        string name,
        string filterText,
        bool isFavorite,
        DateTimeOffset? lastUsed = null,
        ImmutableList<string>? tags = null)
    {
        var filter = SavedFilter.TryCreate(filterText);
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
            IsFavorite = isFavorite,
            LastUsedUtc = isFavorite ? null : lastUsed,
            Tags = tags ?? [],
        };
    }

    private static FilterRow NewRowWithLibraryEntries(params LibraryEntry[] entries)
    {
        var stateMock = Substitute.For<IState<FilterLibraryState>>();
        stateMock.Value.Returns(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.CreateRange(entries),
        });
        return NewRowWithState(stateMock);
    }

    private static FilterRow NewRowWithState(IState<FilterLibraryState> stateMock)
    {
        var row = new FilterRow();
        var prop = typeof(FilterRow).GetProperty(
            "FilterLibraryState",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        prop.SetValue(row, stateMock);
        return row;
    }
}

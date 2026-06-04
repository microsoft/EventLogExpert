// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class LibraryEntryPolymorphicJsonTests
{
    [Fact]
    public void Deserialize_MissingKind_ThrowsNotSupportedException()
    {
        var json = """
            {"Id":"00000000-0000-0000-0000-000000000001","Name":"y","CreatedUtc":"2026-05-31T12:00:00+00:00"}
            """;

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void Deserialize_TagsExplicitNull_GuardedToEmptyImmutableList()
    {
        var json = """
            {"Kind":"Filter","Id":"00000000-0000-0000-0000-000000000011","Name":"NullTags","CreatedUtc":"2026-05-31T12:00:00+00:00","tags":null,"Filter":{"Color":0,"ComparisonText":"Level == 4","IsExcluded":false,"Mode":"Advanced"}}
            """;

        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        var typed = Assert.IsType<LibraryEntrySavedFilter>(restored);
        Assert.NotNull(typed.Tags);
        Assert.Empty(typed.Tags);
    }

    [Fact]
    public void Deserialize_TagsNonStringArrayElement_ThrowsJsonException()
    {
        var json = """
            {"Kind":"Filter","Id":"00000000-0000-0000-0000-000000000013","Name":"BadTags","CreatedUtc":"2026-05-31T12:00:00+00:00","tags":["valid",42],"Filter":{"Color":0,"ComparisonText":"Level == 4","IsExcluded":false,"Mode":"Advanced"}}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void Deserialize_TagsPropertyMissing_DefaultsToEmptyImmutableList()
    {
        var json = """
            {"Kind":"Filter","Id":"00000000-0000-0000-0000-000000000010","Name":"NoTags","CreatedUtc":"2026-05-31T12:00:00+00:00","Filter":{"Color":0,"ComparisonText":"Level == 4","IsExcluded":false,"Mode":"Advanced"}}
            """;

        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        var typed = Assert.IsType<LibraryEntrySavedFilter>(restored);
        Assert.NotNull(typed.Tags);
        Assert.Empty(typed.Tags);
    }

    [Fact]
    public void Deserialize_UnknownKind_ThrowsJsonException()
    {
        var json = """
            {"Kind":"Unknown","Id":"00000000-0000-0000-0000-000000000001","Name":"y","CreatedUtc":"2026-05-31T12:00:00+00:00"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void LibraryEntryIdJsonConverter_EmptyString_ThrowsJsonException()
    {
        var json = """{"Kind":"Filter","Id":"","Name":"x","CreatedUtc":"2026-05-31T12:00:00+00:00","Filter":{"Color":0,"ComparisonText":"Level == 4","IsExcluded":false,"Mode":"Advanced"}}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void LibraryEntryIdJsonConverter_MalformedGuid_ThrowsJsonException()
    {
        var json = """{"Kind":"Filter","Id":"not-a-guid","Name":"x","CreatedUtc":"2026-05-31T12:00:00+00:00","Filter":{"Color":0,"ComparisonText":"Level == 4","IsExcluded":false,"Mode":"Advanced"}}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void LibraryEntryIdJsonConverter_NonStringToken_ThrowsJsonException()
    {
        // GUID written as JSON number, not string.
        var json = """{"Kind":"Filter","Id":12345,"Name":"x","CreatedUtc":"2026-05-31T12:00:00+00:00","Filter":{"Color":0,"ComparisonText":"Level == 4","IsExcluded":false,"Mode":"Advanced"}}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void RoundTrip_FilterSetEntry_PreservesAllFields_WithKindDiscriminator()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var createdUtc = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var filterSetId = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        LibraryEntry entry = new LibraryEntryFilterSet
        {
            Id = filterSetId,
            Name = "My Preset",
            CreatedUtc = createdUtc,
            Filters = [f1, f2],
        };

        var json = JsonSerializer.Serialize(entry);
        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        var restoredFilterSet = Assert.IsType<LibraryEntryFilterSet>(restored);
        Assert.Equal(filterSetId, restoredFilterSet.Id);
        Assert.Equal("My Preset", restoredFilterSet.Name);
        Assert.Equal(createdUtc, restoredFilterSet.CreatedUtc);
        Assert.Equal(2, restoredFilterSet.Filters.Count);
        Assert.Equal("Level == 2", restoredFilterSet.Filters[0].ComparisonText);
        Assert.Equal("Level == 4", restoredFilterSet.Filters[1].ComparisonText);
        Assert.Contains("\"Kind\":\"FilterSet\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_LibraryEntry_PreservesOriginIsFavoriteAndLastUsedUtc()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var lastUsed = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);

        LibraryEntry entry = new LibraryEntrySavedFilter
        {
            Id = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-000000000003")),
            Name = "Auto-tracked Fav",
            CreatedUtc = lastUsed,
            IsFavorite = true,
            LastUsedUtc = lastUsed,
            Origin = LibraryEntryOrigin.AutoTracked,
            Filter = filter,
        };

        var json = JsonSerializer.Serialize(entry);
        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        var restoredFilter = Assert.IsType<LibraryEntrySavedFilter>(restored);
        Assert.True(restoredFilter.IsFavorite);
        Assert.Equal(lastUsed, restoredFilter.LastUsedUtc);
        Assert.Equal(LibraryEntryOrigin.AutoTracked, restoredFilter.Origin);
        Assert.Contains("\"Origin\":\"AutoTracked\"", json, StringComparison.Ordinal);
        Assert.Contains("\"IsFavorite\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_SavedFilterEntry_PreservesAllFields_WithKindDiscriminator()
    {
        var filter = SavedFilter.TryCreate("Level == 4", color: HighlightColor.Blue, isExcluded: true);
        Assert.NotNull(filter);

        var createdUtc = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var filterId = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        LibraryEntry entry = new LibraryEntrySavedFilter
        {
            Id = filterId,
            Name = "My Filter",
            CreatedUtc = createdUtc,
            Filter = filter,
        };

        var json = JsonSerializer.Serialize(entry);
        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        var restoredFilter = Assert.IsType<LibraryEntrySavedFilter>(restored);
        Assert.Equal(filterId, restoredFilter.Id);
        Assert.Equal("My Filter", restoredFilter.Name);
        Assert.Equal(createdUtc, restoredFilter.CreatedUtc);
        Assert.Equal("Level == 4", restoredFilter.Filter.ComparisonText);
        Assert.Equal(HighlightColor.Blue, restoredFilter.Filter.Color);
        Assert.True(restoredFilter.Filter.IsExcluded);
        Assert.NotNull(restoredFilter.Filter.Compiled);
        Assert.Contains("\"Kind\":\"Filter\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_TagsPreservedInOrder()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        LibraryEntry entry = new LibraryEntrySavedFilter
        {
            Id = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-000000000012")),
            Name = "Tagged",
            CreatedUtc = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero),
            Tags = ["zeta", "alpha", "mike"],
            Filter = filter,
        };

        var json = JsonSerializer.Serialize(entry);
        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        var typed = Assert.IsType<LibraryEntrySavedFilter>(restored);
        Assert.Equal(["zeta", "alpha", "mike"], typed.Tags);
        Assert.Contains("\"tags\":[\"zeta\",\"alpha\",\"mike\"]", json, StringComparison.Ordinal);
    }
}

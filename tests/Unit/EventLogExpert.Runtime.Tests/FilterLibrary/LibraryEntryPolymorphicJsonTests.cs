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
    public void Deserialize_UnknownKind_ThrowsJsonException()
    {
        var json = """
            {"Kind":"Unknown","Id":"00000000-0000-0000-0000-000000000001","Name":"y","CreatedUtc":"2026-05-31T12:00:00+00:00"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void RoundTrip_PresetEntry_PreservesAllFields_WithKindDiscriminator()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var createdUtc = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var presetId = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        LibraryEntry entry = new LibraryEntryPreset
        {
            Id = presetId,
            Name = "My Preset",
            CreatedUtc = createdUtc,
            Filters = [f1, f2],
        };

        var json = JsonSerializer.Serialize(entry);
        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        var restoredPreset = Assert.IsType<LibraryEntryPreset>(restored);
        Assert.Equal(presetId, restoredPreset.Id);
        Assert.Equal("My Preset", restoredPreset.Name);
        Assert.Equal(createdUtc, restoredPreset.CreatedUtc);
        Assert.Equal(2, restoredPreset.Filters.Count);
        Assert.Equal("Level == 2", restoredPreset.Filters[0].ComparisonText);
        Assert.Equal("Level == 4", restoredPreset.Filters[1].ComparisonText);
        Assert.Contains("\"Kind\":\"Preset\"", json, StringComparison.Ordinal);
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
}

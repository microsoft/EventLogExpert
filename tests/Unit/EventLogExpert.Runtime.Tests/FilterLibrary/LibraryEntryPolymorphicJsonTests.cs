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
            {"Id":"x","Name":"y","CreatedUtc":"2026-05-31T12:00:00+00:00"}
            """;

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void Deserialize_UnknownKind_ThrowsJsonException()
    {
        var json = """
            {"Kind":"Unknown","Id":"x","Name":"y","CreatedUtc":"2026-05-31T12:00:00+00:00"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryEntry>(json));
    }

    [Fact]
    public void RoundTrip_PresetEntry_PreservesAllFields_WithKindDiscriminator()
    {
        // Arrange
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var createdUtc = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        LibraryEntry entry = new LibraryEntryPreset("id-2", "My Preset", createdUtc, [f1, f2]);

        // Act
        var json = JsonSerializer.Serialize(entry);
        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        // Assert
        var restoredPreset = Assert.IsType<LibraryEntryPreset>(restored);
        Assert.Equal("id-2", restoredPreset.Id);
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
        // Arrange
        var filter = SavedFilter.TryCreate("Level == 4", color: HighlightColor.Blue, isExcluded: true);
        Assert.NotNull(filter);

        var createdUtc = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        LibraryEntry entry = new LibraryEntrySavedFilter("id-1", "My Filter", createdUtc, filter);

        // Act
        var json = JsonSerializer.Serialize(entry);
        var restored = JsonSerializer.Deserialize<LibraryEntry>(json);

        // Assert
        var restoredFilter = Assert.IsType<LibraryEntrySavedFilter>(restored);
        Assert.Equal("id-1", restoredFilter.Id);
        Assert.Equal("My Filter", restoredFilter.Name);
        Assert.Equal(createdUtc, restoredFilter.CreatedUtc);
        Assert.Equal("Level == 4", restoredFilter.Filter.ComparisonText);
        Assert.Equal(HighlightColor.Blue, restoredFilter.Filter.Color);
        Assert.True(restoredFilter.Filter.IsExcluded);
        Assert.NotNull(restoredFilter.Filter.Compiled);
        Assert.Contains("\"Kind\":\"Filter\"", json, StringComparison.Ordinal);
    }
}

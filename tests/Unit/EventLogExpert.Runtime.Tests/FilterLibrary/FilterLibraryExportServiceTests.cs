// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class FilterLibraryExportServiceTests
{
    private readonly FilterLibraryExportService _service = new();

    [Fact]
    public void Deserialize_EmptyString_ReturnsErrorWithoutThrowing()
    {
        var preflight = _service.Deserialize(string.Empty, []);

        Assert.NotNull(preflight.Error);
    }

    [Fact]
    public void Deserialize_ExactDuplicate_BySavedFilterFingerprint_RoutesToSkipped()
    {
        var existing = BuildSavedEntry("entry", comparisonText: "Level == 4");
        var incoming = BuildSavedEntry("different-name", comparisonText: "Level == 4");
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existing]);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.SkippedDuplicates);
    }

    [Fact]
    public void Deserialize_IncomingIdCollidesWithExistingId_RegeneratesIdToAvoidCrash()
    {
        var existing = BuildSavedEntry("existing-name", comparisonText: "Level == 4");
        var incomingIdCollision = BuildSavedEntry("brand-new-name", comparisonText: "Level == 5") with { Id = existing.Id };
        var json = _service.Serialize([incomingIdCollision]);

        var preflight = _service.Deserialize(json, [existing]);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
        Assert.NotEqual(existing.Id, preflight.ToAdd[0].Id);
    }

    [Fact]
    public void Deserialize_LegacySavedFilterGroupArray_ConvertsToFilterSets()
    {
        var legacyJson = """
            [
                {
                    "Name": "Exchange\\HUB",
                    "Filters": [
                        {
                            "Color": "None",
                            "ComparisonText": "Level == 4",
                            "Mode": "Basic"
                        }
                    ]
                },
                {
                    "Name": "Sharepoint",
                    "Filters": [
                        {
                            "Color": "None",
                            "ComparisonText": "Level == 5",
                            "Mode": "Basic"
                        }
                    ]
                }
            ]
            """;

        var preflight = _service.Deserialize(legacyJson, []);

        Assert.Null(preflight.Error);
        Assert.Equal(2, preflight.ToAdd.Count);
        Assert.All(preflight.ToAdd, e => Assert.IsType<LibraryEntryFilterSet>(e));
        Assert.Contains(preflight.ToAdd, e => e.Name == "Exchange\\HUB");
        Assert.Contains(preflight.ToAdd, e => e.Name == "Sharepoint");
    }

    [Fact]
    public void Deserialize_LegacySavedFilterGroupArray_SkipsEmptyGroups()
    {
        var legacyJson = """
            [
                { "Name": "Empty", "Filters": [] },
                { "Name": "HasFilter", "Filters": [{ "Color": "None", "ComparisonText": "Level == 4", "Mode": "Basic" }] }
            ]
            """;

        var preflight = _service.Deserialize(legacyJson, []);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
        Assert.Equal("HasFilter", preflight.ToAdd[0].Name);
    }

    [Fact]
    public void Deserialize_MalformedJson_ReturnsParseErrorWithoutThrowing()
    {
        var preflight = _service.Deserialize("{this is not valid json", []);

        Assert.NotNull(preflight.Error);
        Assert.Empty(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_NameConflict_RoutesEntryToToReplace()
    {
        var existing = BuildSavedEntry("dup", comparisonText: "Level == 4");
        var incoming = BuildSavedEntry("dup", comparisonText: "Level == 5");
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existing]);

        Assert.Null(preflight.Error);
        Assert.Empty(preflight.ToAdd);
        Assert.Single(preflight.ToReplace);
        Assert.Equal(existing.Id, preflight.ToReplace[0].Existing.Id);
        Assert.Equal(incoming.Name, preflight.ToReplace[0].Incoming.Name);
    }

    [Fact]
    public void Deserialize_NoMatch_RoutesToToAdd()
    {
        var existing = BuildSavedEntry("existing", comparisonText: "Level == 4");
        var incoming = BuildSavedEntry("brand-new", comparisonText: "Level == 5");
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existing]);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_NoSchemaVersion_FallsBackToBareArrayParse()
    {
        var entry = BuildSavedEntry("legacy");
        var bareArrayJson = JsonSerializer.Serialize<List<LibraryEntry>>([entry]);

        var preflight = _service.Deserialize(bareArrayJson, []);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_NullExisting_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Deserialize("[]", null!));
    }

    [Fact]
    public void Deserialize_SchemaVersionGreaterThanCurrent_ReturnsUnsupportedError()
    {
        var json = """{"schemaVersion": 2, "entries": []}""";

        var preflight = _service.Deserialize(json, []);

        Assert.NotNull(preflight.Error);
        Assert.Contains("Unsupported schema version 2", preflight.Error!);
        Assert.Empty(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_SchemaVersionLessThanOne_ReturnsInvalidError()
    {
        var json = """{"schemaVersion": 0, "entries": []}""";

        var preflight = _service.Deserialize(json, []);

        Assert.NotNull(preflight.Error);
        Assert.Contains("Invalid schema version 0", preflight.Error!);
    }

    [Fact]
    public void Deserialize_WithDuplicateExistingFilterSets_DoesNotThrow_RoutesIncomingToSkipped()
    {
        var existingA = BuildFilterSet("alpha", BuildSavedFilter("Level == 4"));
        var existingB = BuildFilterSet("alpha", BuildSavedFilter("Level == 4"));
        var incoming = BuildFilterSet("alpha", BuildSavedFilter("Level == 4"));
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existingA, existingB]);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.SkippedDuplicates);
    }

    [Fact]
    public void Deserialize_WithDuplicateExistingSavedFilters_DoesNotThrow_RoutesIncomingToSkipped()
    {
        var existingA = BuildSavedEntry("first", comparisonText: "Level == 4");
        var existingB = BuildSavedEntry("second", comparisonText: "Level == 4");
        var incoming = BuildSavedEntry("third", comparisonText: "Level == 4");
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existingA, existingB]);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.SkippedDuplicates);
    }

    [Fact]
    public void Deserialize_WithDuplicateNamesWithinIncomingFile_KeepsFirst_SkipsLater()
    {
        var entry1 = BuildSavedEntry("duplicate", comparisonText: "Level == 4");
        var entry2 = BuildSavedEntry("DUPLICATE", comparisonText: "Level == 5");
        var json = _service.Serialize([entry1, entry2]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
        Assert.Single(preflight.SkippedDuplicates);
        Assert.Equal("duplicate", preflight.ToAdd[0].Name);
    }

    [Fact]
    public void RoundTrip_PreservesFilterSetEntry_AndKind()
    {
        var entry = BuildFilterSet("set", BuildSavedFilter("Level == 4"), BuildSavedFilter("Level == 5"));
        var json = _service.Serialize([entry]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        var deserialized = Assert.IsType<LibraryEntryFilterSet>(preflight.ToAdd[0]);
        Assert.Equal(entry.Name, deserialized.Name);
        Assert.Equal(2, deserialized.Filters.Count);
    }

    [Fact]
    public void RoundTrip_PreservesSavedFilterEntry()
    {
        var entry = BuildSavedEntry("test");
        var json = _service.Serialize([entry]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
        var deserialized = Assert.IsType<LibraryEntrySavedFilter>(preflight.ToAdd[0]);
        Assert.Equal(entry.Name, deserialized.Name);
        Assert.Equal(entry.Filter.ComparisonText, deserialized.Filter.ComparisonText);
        Assert.Equal(entry.Id, deserialized.Id);
    }

    [Fact]
    public void Serialize_NullEntries_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Serialize(null!));
    }

    [Fact]
    public void Serialize_ProducesExactJsonPropertyNames_SchemaVersionAndEntries()
    {
        var json = _service.Serialize([BuildSavedEntry("a")]);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("entries").ValueKind);
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name, params SavedFilter[] filters) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. filters],
        };

    private static LibraryEntrySavedFilter BuildSavedEntry(string name, string comparisonText = "Level == 4")
    {
        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = BuildSavedFilter(comparisonText),
        };
    }

    private static SavedFilter BuildSavedFilter(string comparisonText)
    {
        var filter = SavedFilter.TryCreate(comparisonText);
        Assert.NotNull(filter);
        return filter;
    }
}

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
    public void Deserialize_BasicFilterWithDegenerateTextButCleanBlob_ReturnsError()
    {
        var clean = BuildBasicSavedFilter("Source == \"Foo\"");
        Assert.NotNull(clean.BasicFilter);

        var divergent = clean with
        {
            ComparisonText = "(new[] {\"\", \"mshta.exe\"}).Any(e => Source.Contains(e))",
        };
        var entry = new LibraryEntrySavedFilter
        {
            Name = "stale-blob",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = divergent,
        };
        var json = _service.Serialize([entry]);

        var preflight = _service.Deserialize(json, []);

        Assert.NotNull(preflight.Error);
        Assert.Empty(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_DuplicateNamesWithinIncomingFile_DifferentFilters_NoExisting_BothImported()
    {
        var entry1 = BuildSavedEntry("duplicate", comparisonText: "Level == 4");
        var entry2 = BuildSavedEntry("DUPLICATE", comparisonText: "Level == 5");
        var json = _service.Serialize([entry1, entry2]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        Assert.Equal(2, preflight.ToAdd.Count);
        Assert.Empty(preflight.SkippedDuplicates);
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsErrorWithoutThrowing()
    {
        var preflight = _service.Deserialize(string.Empty, []);

        Assert.NotNull(preflight.Error);
    }

    [Fact]
    public void Deserialize_FeatureDisabled_ExistingBackslashEntry_StillMatchesIncomingMigratedEquivalent()
    {
        var existing = BuildSavedEntry(@"Network\DNS", comparisonText: "Level == 4");
        var incomingMigrated = BuildSavedEntry("DNS", comparisonText: "Level == 4") with
        {
            Tags = ["network"],
        };
        var json = _service.Serialize([incomingMigrated]);

        using var _ = BackslashMigrationFeature.Override(false);

        var preflight = _service.Deserialize(json, [existing]);

        Assert.False(preflight.ImportBlocked);
        Assert.Empty(preflight.ToAdd);
        Assert.Single(preflight.SkippedDuplicates);
    }

    [Fact]
    public void Deserialize_FeatureDisabled_IncomingCleanNames_ProceedsNormally()
    {
        var clean = BuildSavedEntry("CleanName");
        var json = _service.Serialize([clean]);

        using var _ = BackslashMigrationFeature.Override(false);

        var preflight = _service.Deserialize(json, []);

        Assert.False(preflight.ImportBlocked);
        Assert.Single(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_FeatureDisabled_IncomingHasBackslashName_ReturnsBlockedPreflight()
    {
        var dirty = BuildSavedEntry(@"Network\DNS");
        var json = _service.Serialize([dirty]);

        using var _ = BackslashMigrationFeature.Override(false);

        var preflight = _service.Deserialize(json, []);

        Assert.True(preflight.ImportBlocked);
        Assert.Single(preflight.InvalidLegacyNames);
        Assert.Equal(@"Network\DNS", preflight.InvalidLegacyNames[0]);
        Assert.Empty(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_IncomingBasicFilterThatDoesNotHydrate_ReturnsError()
    {
        var unhydratable = SavedFilter.TryCreate(
            "(new[] {\"\", \"mshta.exe\"}).Any(e => Source.Contains(e))",
            mode: FilterMode.Basic);
        Assert.NotNull(unhydratable);
        Assert.Null(unhydratable.BasicFilter);

        var entry = new LibraryEntrySavedFilter
        {
            Name = "non-canonical",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = unhydratable,
        };
        var json = _service.Serialize([entry]);

        var preflight = _service.Deserialize(json, []);

        Assert.NotNull(preflight.Error);
        Assert.Empty(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_IncomingBasicFilterWithEmptyContainsAmongRealValues_IsFlaggedNotError()
    {
        var flagged = BuildBasicSavedEntry("match-all",
            "(new[] {\"\", \"mshta.exe\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var json = _service.Serialize([flagged]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        Assert.Contains("match-all", preflight.NormalizableEmptyValueEntryNames);
        Assert.Single(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_IncomingBasicFilterWithEmptyNotContainsValue_IsFlagged()
    {
        var flagged = BuildBasicSavedEntry("match-none",
            "!(new[] {\"\", \"mshta.exe\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var json = _service.Serialize([flagged]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        Assert.Contains("match-none", preflight.NormalizableEmptyValueEntryNames);
        Assert.Single(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_IncomingBasicFilterWithEqualsEmptyValue_IsNotFlagged()
    {
        var equalsEmpty = BuildBasicSavedEntry("equals-empty", "(new[] {\"\", \"System\"}).Contains(Source)");
        var json = _service.Serialize([equalsEmpty]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        Assert.Empty(preflight.NormalizableEmptyValueEntryNames);
        Assert.Single(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_IncomingBasicFilterWithoutEmptyValue_IsNotFlagged()
    {
        var clean = BuildBasicSavedEntry("clean",
            "(new[] {\"mshta.exe\", \"wscript.exe\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var json = _service.Serialize([clean]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        Assert.Empty(preflight.NormalizableEmptyValueEntryNames);
        Assert.Single(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_IncomingFilterSetWithEmptyValueMember_IsFlaggedNotError()
    {
        var clean = BuildBasicSavedFilter("Source == \"Foo\"");
        var flagged = BuildBasicSavedFilter(
            "(new[] {\"\", \"mshta.exe\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var set = new LibraryEntryFilterSet
        {
            Name = "set-with-empty-value",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [clean, flagged],
        };
        var json = _service.Serialize([set]);

        var preflight = _service.Deserialize(json, []);

        Assert.Null(preflight.Error);
        Assert.Contains("set-with-empty-value", preflight.NormalizableEmptyValueEntryNames);
        Assert.Single(preflight.ToAdd);
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
        Assert.Contains(preflight.ToAdd, e => e.Name == "HUB" && e.Tags.SequenceEqual(new[] { "exchange" }, StringComparer.Ordinal));
        Assert.Contains(preflight.ToAdd, e => e.Name == "Sharepoint" && e.Tags.Count == 0);
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
    public void Deserialize_LocalRawMatchesIncomingMigrated_RoutesToSkippedDuplicates()
    {
        var existing = BuildFilterSet(@"Network\DNS", BuildSavedFilter("Level == 4"));
        var incomingRaw = BuildFilterSet(@"Network\DNS", BuildSavedFilter("Level == 4"));
        var json = _service.Serialize([incomingRaw]);

        var preflight = _service.Deserialize(json, [existing]);

        Assert.Null(preflight.Error);
        Assert.Empty(preflight.ToAdd);
        Assert.Empty(preflight.ToReplace);
        Assert.Empty(preflight.ToUpdate);
        Assert.Single(preflight.SkippedDuplicates);
    }

    [Fact]
    public void Deserialize_LocalUnmigratedMatchesIncomingTagsOnly_RoutesToToUpdate()
    {
        var existing = BuildFilterSet(@"Network\DNS", BuildSavedFilter("Level == 4"));
        var incoming = BuildFilterSet("DNS", BuildSavedFilter("Level == 4")) with { Tags = ["network", "extra-tag"] };
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existing]);

        Assert.Null(preflight.Error);
        Assert.Empty(preflight.ToAdd);
        Assert.Empty(preflight.ToReplace);
        Assert.Single(preflight.ToUpdate);
        Assert.Equal(existing.Id, preflight.ToUpdate[0].Existing.Id);
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
        Assert.Empty(preflight.ToUpdate);
        Assert.Empty(preflight.AmbiguousMatches);
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
    public void Deserialize_NormalizeEmptyContainsAmongRealValues_KeepsFilterWithEmptyStripped()
    {
        var flagged = BuildBasicSavedEntry("match-all",
            "(new[] {\"\", \"mshta.exe\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var json = _service.Serialize([flagged]);

        var preflight = _service.Deserialize(json, [], normalizeEmptyValues: true);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
        Assert.Empty(preflight.NormalizeRemovedFilterNames);

        var imported = Assert.IsType<LibraryEntrySavedFilter>(preflight.ToAdd[0]);
        Assert.NotNull(imported.Filter.BasicFilter);
        Assert.DoesNotContain("", imported.Filter.BasicFilter.Comparison.Values);
        Assert.Contains("mshta.exe", imported.Filter.BasicFilter.Comparison.Values);
    }

    [Fact]
    public void Deserialize_NormalizeFilterSetLosingOneMember_KeepsSetWithoutIt()
    {
        var clean = BuildBasicSavedFilter("Source == \"Foo\"");
        var onlyEmpty = BuildBasicSavedFilter(
            "(new[] {\"\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var set = new LibraryEntryFilterSet
        {
            Name = "partly-empty-set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [clean, onlyEmpty],
        };
        var json = _service.Serialize([set]);

        var preflight = _service.Deserialize(json, [], normalizeEmptyValues: true);

        Assert.Null(preflight.Error);
        var imported = Assert.IsType<LibraryEntryFilterSet>(Assert.Single(preflight.ToAdd));
        Assert.Single(imported.Filters);
        Assert.Contains("partly-empty-set", preflight.NormalizeRemovedFilterNames);
    }

    [Fact]
    public void Deserialize_NormalizeFilterSetWithAllMembersEmpty_RemovesSet()
    {
        var onlyEmptyA = BuildBasicSavedFilter(
            "(new[] {\"\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var onlyEmptyB = BuildBasicSavedFilter(
            "!(new[] {\"\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var set = new LibraryEntryFilterSet
        {
            Name = "all-empty-set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [onlyEmptyA, onlyEmptyB],
        };
        var json = _service.Serialize([set]);

        var preflight = _service.Deserialize(json, [], normalizeEmptyValues: true);

        Assert.Null(preflight.Error);
        Assert.Empty(preflight.ToAdd);
        Assert.Contains("all-empty-set", preflight.NormalizeRemovedFilterNames);
    }

    [Fact]
    public void Deserialize_NormalizeStandaloneFilterWithOnlyEmptyValue_DropsAndReportsIt()
    {
        var onlyEmpty = BuildBasicSavedEntry("only-empty",
            "(new[] {\"\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var json = _service.Serialize([onlyEmpty]);

        var flaggedPreflight = _service.Deserialize(json, []);
        Assert.Null(flaggedPreflight.Error);
        Assert.Contains("only-empty", flaggedPreflight.NormalizableEmptyValueEntryNames);

        var normalized = _service.Deserialize(json, [], normalizeEmptyValues: true);
        Assert.Null(normalized.Error);
        Assert.Empty(normalized.ToAdd);
        Assert.Contains("only-empty", normalized.NormalizeRemovedFilterNames);
    }

    [Fact]
    public void Deserialize_NormalizeWithUnrelatedEmptySet_PreservesTheEmptySet()
    {
        var flagged = BuildBasicSavedEntry("match-all",
            "(new[] {\"\", \"mshta.exe\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))");
        var emptySet = new LibraryEntryFilterSet
        {
            Name = "empty-set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [],
        };
        var json = _service.Serialize([flagged, emptySet]);

        var preflight = _service.Deserialize(json, [], normalizeEmptyValues: true);

        Assert.Null(preflight.Error);
        Assert.Contains(preflight.ToAdd, entry => entry is LibraryEntryFilterSet { Name: "empty-set" });
        Assert.DoesNotContain("empty-set", preflight.NormalizeRemovedFilterNames);
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
    public void Deserialize_SameSavedFilterContentDifferentNames_BothRouteToToAdd()
    {
        var existing = BuildSavedEntry("entry", comparisonText: "Level == 4");
        var incoming = BuildSavedEntry("different-name", comparisonText: "Level == 4");
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existing]);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
        Assert.Empty(preflight.SkippedDuplicates);
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
    public void Deserialize_TwoIncomingShareMigratedNameDifferentTags_BothImported()
    {
        var legacyJson = """
            [
                {
                    "Name": "A\\Child",
                    "Filters": [
                        { "Color": "None", "ComparisonText": "Level == 4", "Mode": "Basic" }
                    ]
                },
                {
                    "Name": "B\\Child",
                    "Filters": [
                        { "Color": "None", "ComparisonText": "Level == 5", "Mode": "Basic" }
                    ]
                }
            ]
            """;

        var preflight = _service.Deserialize(legacyJson, []);

        Assert.Null(preflight.Error);
        Assert.Equal(2, preflight.ToAdd.Count);
        Assert.Contains(preflight.ToAdd, e => e.Name == "Child" && e.Tags.Contains("a"));
        Assert.Contains(preflight.ToAdd, e => e.Name == "Child" && e.Tags.Contains("b"));
    }

    [Fact]
    public void Deserialize_TwoIncomingShareMigratedNameSameTagsSameFilters_SecondSkipped()
    {
        var legacyJson = """
            [
                {
                    "Name": "A\\Child",
                    "Filters": [
                        { "Color": "None", "ComparisonText": "Level == 4", "Mode": "Basic" }
                    ]
                },
                {
                    "Name": "A\\Child",
                    "Filters": [
                        { "Color": "None", "ComparisonText": "Level == 4", "Mode": "Basic" }
                    ]
                }
            ]
            """;

        var preflight = _service.Deserialize(legacyJson, []);

        Assert.Null(preflight.Error);
        Assert.Single(preflight.ToAdd);
        Assert.Single(preflight.SkippedDuplicates);
    }

    [Fact]
    public void Deserialize_TwoLocalEntriesShareMigratedName_DifferentFilters_IncomingMatch_RoutesToAmbiguousMatches()
    {
        var existingA = BuildSavedEntry("Child", comparisonText: "Level == 4") with { Tags = ["a"] };
        var existingB = BuildSavedEntry("Child", comparisonText: "Level == 5") with { Tags = ["b"] };
        var incoming = BuildSavedEntry("Child", comparisonText: "Level == 6");
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existingA, existingB]);

        Assert.Null(preflight.Error);
        Assert.Empty(preflight.ToAdd);
        Assert.Empty(preflight.ToReplace);
        Assert.Empty(preflight.ToUpdate);
        Assert.Single(preflight.AmbiguousMatches);
        var ambiguous = preflight.AmbiguousMatches[0];
        Assert.Equal(2, ambiguous.Candidates.Count);
    }

    [Fact]
    public void Deserialize_TwoLocalEntriesShareRelaxedKey_IncomingMatch_RoutesToAmbiguousMatches()
    {
        var existingA = BuildSavedEntry("Same", comparisonText: "Level == 4") with { Tags = ["hub"] };
        var existingB = BuildSavedEntry("Same", comparisonText: "Level == 4") with { Tags = ["exchange"] };
        var incoming = BuildSavedEntry("Same", comparisonText: "Level == 4") with { Tags = ["extra"] };
        var json = _service.Serialize([incoming]);

        var preflight = _service.Deserialize(json, [existingA, existingB]);

        Assert.Null(preflight.Error);
        Assert.Empty(preflight.ToUpdate);
        Assert.Single(preflight.AmbiguousMatches);
    }

    [Fact]
    public void Deserialize_VersionedEntryMissingKindDiscriminator_ReturnsErrorWithoutThrowing()
    {
        var json = """{"schemaVersion": 1, "entries": [{"Name": "x"}]}""";

        var preflight = _service.Deserialize(json, []);

        Assert.NotNull(preflight.Error);
        Assert.Empty(preflight.ToAdd);
    }

    [Fact]
    public void Deserialize_VersionedNullEntry_ReturnsErrorWithoutThrowing()
    {
        var json = """{"schemaVersion": 1, "entries": [null]}""";

        var preflight = _service.Deserialize(json, []);

        Assert.NotNull(preflight.Error);
        Assert.Empty(preflight.ToAdd);
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

    private static LibraryEntrySavedFilter BuildBasicSavedEntry(string name, string comparisonText)
    {
        var filter = SavedFilter.TryCreate(comparisonText, mode: FilterMode.Basic);
        Assert.NotNull(filter);
        Assert.NotNull(filter.BasicFilter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }

    private static SavedFilter BuildBasicSavedFilter(string comparisonText)
    {
        var filter = SavedFilter.TryCreate(comparisonText, mode: FilterMode.Basic);
        Assert.NotNull(filter);
        return filter;
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

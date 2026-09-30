// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class FilterImportTextComposerTests
{
    private readonly MarkerLocalizer _markerLocalizer = new();

    public static TheoryData<ImportSummary, string> SummaryMarkerVariants() => new()
    {
        { new ImportSummary(2, 12, 2, 3, 0), "[[FilterImport_Summary_Tag]]" },
        { new ImportSummary(2, 12, 1, 3, 1), "[[FilterImport_Summary_Tag_Ambiguous]]" },
        { new ImportSummary(2, 12, 2, 3, 1), "[[FilterImport_Summary_Tag_Ambiguous]]" }
    };

    [Fact]
    public void EmptyValueMessage_RoutesThroughPluralKey()
    {
        var actual = FilterImportTextComposer.EmptyValueMessage(_markerLocalizer, ["Entry A", "Entry B"]);

        Assert.Equal("[[FilterImport_EmptyValueMessage]]", actual);
    }

    [Fact]
    public void EmptyValueMessage_SingularUsesApprovedGrammarCorrection()
    {
        var actual = WithEnUsCulture(() => FilterImportTextComposer.EmptyValueMessage(BuildLocalizer(), ["Entry A"]));

        Assert.Equal(
            "1 library item contains a filter with an empty value: Entry A. An empty Contains matches every event; " +
            "an empty NotContains matches none. Normalize removes the empty values; if that leaves a Contains/NotContains " +
            "criterion with no values, the whole filter is removed (including its other criteria). Import as-is keeps them " +
            "all as authored. An as-is filter may lose its empty values or need repair if you later edit it in the Basic editor.",
            actual);
    }

    [Theory]
    [InlineData(1, "Removed empty-criterion filters from 1 library item after removing empty values.")]
    [InlineData(2, "Removed empty-criterion filters from 2 library items after removing empty values.")]
    public void ImportConfirmation_RemovedEmptyNotice_UsesApprovedGrammarCorrection(int count, string expectedNotice)
    {
        var preflight = new ImportPreflight([], [], [])
        {
            NormalizeRemovedFilterNames = Enumerable.Range(0, count).Select(index => $"Removed {index}").ToList(),
        };

        var actual = WithEnUsCulture(() => FilterImportTextComposer.ImportConfirmation(
            BuildLocalizer(),
            preflight,
            keptEmptyValuesAsIs: false));

        Assert.StartsWith(expectedNotice + "\n\n", actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, 0, false, "[[FilterImport_PreviewHeader]]\n  • [[FilterImport_Added]]\n  • [[FilterImport_Skipped]]")]
    [InlineData(0, 1, false, "[[FilterImport_RemovedEmptyNotice]]\n\n[[FilterImport_PreviewHeader]]\n  • [[FilterImport_Added]]\n  • [[FilterImport_Skipped]]")]
    [InlineData(2, 1, true, "[[FilterImport_KeepEmpty]]\n[[FilterImport_RemovedEmptyNotice]]\n\n[[FilterImport_PreviewHeader]]\n  • [[FilterImport_Added]]\n  • [[FilterImport_Skipped]]")]
    public void ImportConfirmation_RoutesNoticeCountCombinations(
        int keptCount,
        int removedCount,
        bool keptEmptyValuesAsIs,
        string expected)
    {
        var preflight = new ImportPreflight(
            Enumerable.Range(0, keptCount).Select(index => BuildEntry($"Add {index}")).ToList(),
            [],
            [])
        {
            NormalizableEmptyValueEntryNames = Enumerable.Range(0, keptCount).Select(index => $"Keep {index}").ToList(),
            NormalizeRemovedFilterNames = Enumerable.Range(0, removedCount).Select(index => $"Remove {index}").ToList(),
        };

        Assert.Equal(expected, FilterImportTextComposer.ImportConfirmation(_markerLocalizer, preflight, keptEmptyValuesAsIs));
    }

    [Fact]
    public void ImportConfirmation_WithBothNotices_JoinsNoticesAndPreviewWithLfOnly()
    {
        var preflight = new ImportPreflight([BuildEntry("Added")], [], [BuildEntry("Skipped")])
        {
            NormalizableEmptyValueEntryNames = ["Keep A"],
            NormalizeRemovedFilterNames = ["Removed A", "Removed B"],
        };

        var actual = FilterImportTextComposer.ImportConfirmation(_markerLocalizer, preflight, keptEmptyValuesAsIs: true);

        Assert.Equal(
            "[[FilterImport_KeepEmpty]]\n" +
            "[[FilterImport_RemovedEmptyNotice]]\n\n" +
            "[[FilterImport_PreviewHeader]]\n" +
            "  • [[FilterImport_Added]]\n" +
            "  • [[FilterImport_Skipped]]",
            actual);
    }

    [Fact]
    public void NothingToImport_InflectsItemAndDuplicateNounsIndependently()
    {
        string bothOne = WithEnUsCulture(() => FilterImportTextComposer.NothingToImport(BuildLocalizer(), BuildNothingToImportPreflight(1, 1)));
        string itemManyDupOne = WithEnUsCulture(() => FilterImportTextComposer.NothingToImport(BuildLocalizer(), BuildNothingToImportPreflight(71, 1)));
        string itemOneDupMany = WithEnUsCulture(() => FilterImportTextComposer.NothingToImport(BuildLocalizer(), BuildNothingToImportPreflight(1, 93)));

        // Nested plural: the item count and the duplicate count each drive their own noun independently. Normalizing
        // numbers isolates each inflection so the assertions survive copy edits to the surrounding sentence.
        Assert.NotEqual(NormalizeNumbers(bothOne), NormalizeNumbers(itemManyDupOne));
        Assert.NotEqual(NormalizeNumbers(bothOne), NormalizeNumbers(itemOneDupMany));

        // Each count must land in BOTH of its branches, matched on numeric-token boundaries: item as bounded "1"
        // (singular, from the 1/93 render) and 71 (plural, from the 71/1 render); duplicate as bounded "1" (singular,
        // from the 71/1 render) and 93 (plural, from the 1/93 render). So a count dropped from EITHER of its branches
        // is caught.
        Assert.Matches(@"(?<![\d,])1(?![\d,])", itemOneDupMany);
        Assert.Contains("71", itemManyDupOne, StringComparison.Ordinal);
        Assert.Matches(@"(?<![\d,])1(?![\d,])", itemManyDupOne);
        Assert.Contains("93", itemOneDupMany, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1, "[[FilterImport_NothingToImport]]")]
    [InlineData(1, 2, "[[FilterImport_NothingToImport]]")]
    [InlineData(2, 1, "[[FilterImport_NothingToImport]]")]
    [InlineData(2, 2, "[[FilterImport_NothingToImport]]")]
    public void NothingToImport_RoutesEveryCountCombination(int itemCount, int duplicateCount, string expected)
    {
        var preflight = new ImportPreflight([], [], Enumerable.Range(0, duplicateCount).Select(index => BuildEntry($"Skipped {index}")).ToList())
        {
            NormalizeRemovedFilterNames = Enumerable.Range(0, itemCount).Select(index => $"Removed {index}").ToList(),
        };

        Assert.Equal(expected, FilterImportTextComposer.NothingToImport(_markerLocalizer, preflight));
    }

    [Fact]
    public void Preview_ImportBlockedWithElevenNames_AddsOneMoreSuffix()
    {
        var preflight = ImportPreflight.Blocked(Enumerable.Range(1, 11).Select(index => $"Invalid {index:00}").ToList());

        var actual = FilterImportTextComposer.Preview(_markerLocalizer, preflight);

        Assert.EndsWith("  • [[FilterImport_MoreNames(1)]]", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_ImportBlockedWithTenNames_OmitsTruncationSuffix()
    {
        var preflight = ImportPreflight.Blocked(Enumerable.Range(1, 10).Select(index => $"Invalid {index:00}").ToList());

        var actual = FilterImportTextComposer.Preview(_markerLocalizer, preflight);

        Assert.DoesNotContain("FilterImport_MoreNames", actual);
        Assert.EndsWith("  • Invalid 10", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_ImportBlocked_RoutesHeaderNamesAndTruncation()
    {
        var preflight = ImportPreflight.Blocked(Enumerable.Range(1, 12).Select(index => $"Invalid {index:00}").ToList());

        var actual = FilterImportTextComposer.Preview(_markerLocalizer, preflight);

        Assert.Equal(
            "[[FilterImport_BlockedHeader]]\n" +
            "  • Invalid 01\n" +
            "  • Invalid 02\n" +
            "  • Invalid 03\n" +
            "  • Invalid 04\n" +
            "  • Invalid 05\n" +
            "  • Invalid 06\n" +
            "  • Invalid 07\n" +
            "  • Invalid 08\n" +
            "  • Invalid 09\n" +
            "  • Invalid 10\n" +
            "  • [[FilterImport_MoreNames(2)]]",
            actual);
    }

    [Fact]
    public void Preview_WithRepresentativeImport_ProducesByteIdenticalEnglish()
    {
        var preflight = RepresentativePreflight();

        var actual = WithEnUsCulture(() => FilterImportTextComposer.Preview(BuildLocalizer(), preflight));

        Assert.Equal(
            "Import preview:\n" +
            "  • 2 new entries will be added\n" +
            "  • 12 existing entries WILL BE OVERWRITTEN (current filter content will be lost)\n" +
            "Names being overwritten:\n" +
            "  • Overwrite 01\n" +
            "  • Overwrite 02\n" +
            "  • Overwrite 03\n" +
            "  • Overwrite 04\n" +
            "  • Overwrite 05\n" +
            "  • Overwrite 06\n" +
            "  • Overwrite 07\n" +
            "  • Overwrite 08\n" +
            "  • Overwrite 09\n" +
            "  • Overwrite 10\n" +
            "  • ...and 2 more\n" +
            "  • 3 entries will be updated with tag changes\n" +
            "  • 1 existing entry will also be renamed (folder paths → tags)\n" +
            "  • 1 ambiguous entry will be imported as new\n" +
            "  • 3 exact duplicates will be skipped",
            actual);
    }

    [Fact]
    public void Preview_WithRepresentativeImport_RoutesEveryBranchThroughExpectedKeys()
    {
        var preflight = RepresentativePreflight();

        var actual = FilterImportTextComposer.Preview(_markerLocalizer, preflight);

        Assert.Equal(
            "[[FilterImport_PreviewHeader]]\n" +
            "  • [[FilterImport_Added]]\n" +
            "  • [[FilterImport_Overwrite]]\n" +
            "[[FilterImport_OverwriteNamesHeader]]\n" +
            "  • Overwrite 01\n" +
            "  • Overwrite 02\n" +
            "  • Overwrite 03\n" +
            "  • Overwrite 04\n" +
            "  • Overwrite 05\n" +
            "  • Overwrite 06\n" +
            "  • Overwrite 07\n" +
            "  • Overwrite 08\n" +
            "  • Overwrite 09\n" +
            "  • Overwrite 10\n" +
            "  • [[FilterImport_MoreNames(2)]]\n" +
            "  • [[FilterImport_TagUpdates]]\n" +
            "  • [[FilterImport_Renames]]\n" +
            "  • [[FilterImport_Ambiguous]]\n" +
            "  • [[FilterImport_Skipped]]",
            actual);
    }

    [Fact]
    public void Preview_WithStandaloneRename_MatchesOriginalPreflightSummaryByteForByte()
    {
        var preflight = new ImportPreflight(
            [],
            [],
            [],
            [(BuildEntry(@"Folder\Renamed"), BuildEntry("Renamed"))],
            []);

        var actual = WithEnUsCulture(() => FilterImportTextComposer.Preview(BuildLocalizer(), preflight));

        Assert.Equal(OriginalStandaloneRenamePreview(preflight), actual);
    }

    [Theory]
    [MemberData(nameof(SummaryMarkerVariants))]
    public void Summary_RoutesNonDefaultVariantsToExpectedKeys(ImportSummary summary, string expected) =>
        Assert.Equal(expected, FilterImportTextComposer.Summary(_markerLocalizer, summary));

    [Fact]
    public void Summary_TagCount_SelectsSingularOrPluralTagNounAndCarriesEachCount()
    {
        // Mutually non-substring counts for the non-tag fields so a dropped or misplaced argument cannot be masked by
        // another field's digits (unlike "2" hiding inside "12"); only updatedTags varies across the 1/2 boundary.
        var singular = WithEnUsCulture(() => FilterImportTextComposer.Summary(BuildLocalizer(), new ImportSummary(7010, 8020, 1, 9030, 0)));
        var plural = WithEnUsCulture(() => FilterImportTextComposer.Summary(BuildLocalizer(), new ImportSummary(7010, 8020, 4567, 9030, 0)));

        // The updatedTags count drives the tag noun (singular vs plural); normalizing numbers isolates that inflection
        // so the assertion survives copy edits to the surrounding sentence.
        Assert.NotEqual(NormalizeNumbers(singular), NormalizeNumbers(plural));

        // The updatedTags count must render as a standalone token in BOTH branches - "1" in the singular and a distinct
        // value in the plural - so a dropped {updatedTags} is caught in either branch.
        Assert.Matches(@"(?<![\d,])1(?![\d,])", singular);
        Assert.Contains("4567", plural, StringComparison.Ordinal);

        // Every non-tag field's count lands verbatim and ungrouped in both renders.
        foreach (string text in new[] { singular, plural })
        {
            Assert.Contains("7010", text, StringComparison.Ordinal);
            Assert.Contains("8020", text, StringComparison.Ordinal);
            Assert.Contains("9030", text, StringComparison.Ordinal);
            Assert.DoesNotContain("7,010", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TagRenamed_RendersRawUngroupedCountAndCarriesBothTags()
    {
        var large = WithEnUsCulture(() => FilterImportTextComposer.TagRenamed(BuildLocalizer(), "alpha", "bravo", 1000));
        var singular = WithEnUsCulture(() => FilterImportTextComposer.TagRenamed(BuildLocalizer(), "alpha", "bravo", 1));

        // The count renders raw (no thousands separator) in the plural branch and as a bounded "1" in the singular
        // branch, both tag arguments land, and the entry noun inflects - all without pinning the surrounding copy.
        Assert.Contains("1000", large, StringComparison.Ordinal);
        Assert.DoesNotContain("1,000", large, StringComparison.Ordinal);
        Assert.Matches(@"(?<![\d,])1(?![\d,])", singular);
        Assert.Contains("alpha", large, StringComparison.Ordinal);
        Assert.Contains("bravo", large, StringComparison.Ordinal);
        Assert.NotEqual(NormalizeNumbers(singular), NormalizeNumbers(large));
    }

    private static LibraryEntrySavedFilter BuildEntry(string name) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = BuildFilter(),
        };

    private static SavedFilter BuildFilter()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        return filter;
    }

    private static IStringLocalizer<SharedResource> BuildLocalizer() =>
        new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddEventLogLocalization()
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();

    private static ImportPreflight BuildNothingToImportPreflight(int itemCount, int duplicateCount) =>
        new([], [], Enumerable.Range(0, duplicateCount).Select(index => BuildEntry($"Skipped {index}")).ToList())
        {
            NormalizeRemovedFilterNames = Enumerable.Range(0, itemCount).Select(index => $"Removed {index}").ToList(),
        };

    private static string NormalizeNumbers(string value) =>
        Regex.Replace(value, @"\d+(?:,\d{3})*(?:\.\d+)?", "#");

    private static string OriginalStandaloneRenamePreview(ImportPreflight preflight)
    {
        var replacedIds = preflight.ToReplace.Select(pair => pair.Existing.Id).ToHashSet();
        var standaloneTagUpdates = preflight.ToUpdate
            .Select(pair => pair.Existing.Id)
            .Where(id => !replacedIds.Contains(id))
            .Distinct()
            .Count();
        var renameCount = preflight.ToUpdate.Count(pair => pair.Existing.Name.Contains('\\'));

        return "Import preview:\n" +
            $"  • {Plural(preflight.ToAdd.Count, "new entry", "new entries")} will be added\n" +
            $"  • {Plural(standaloneTagUpdates, "entry", "entries")} will be updated with tag changes\n" +
            $"  • {Plural(renameCount, "existing entry", "existing entries")} will also be renamed (folder paths \u2192 tags)\n" +
            $"  • {Plural(preflight.SkippedDuplicates.Count, "exact duplicate", "exact duplicates")} will be skipped";
    }

    private static string Plural(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static ImportPreflight RepresentativePreflight()
    {
        var overwrites = Enumerable.Range(1, 12)
            .Select(index => (Existing: (LibraryEntry)BuildEntry($"Existing {index:00}"), Incoming: (LibraryEntry)BuildEntry($"Overwrite {index:00}")))
            .ToList();
        var updates = new List<(LibraryEntry Existing, LibraryEntry Incoming)>
        {
            (BuildEntry("Tag A"), BuildEntry("Incoming Tag A")),
            (BuildEntry("Tag B"), BuildEntry("Incoming Tag B")),
            (BuildEntry(@"Folder\Renamed"), BuildEntry("Renamed")),
        };

        return new ImportPreflight(
            [BuildEntry("Add A"), BuildEntry("Add B")],
            overwrites,
            [BuildEntry("Skipped A"), BuildEntry("Skipped B"), BuildEntry("Skipped C")],
            updates,
            [([BuildEntry("Candidate")], BuildEntry("Ambiguous"))]);
    }

    private static T WithEnUsCulture<T>(Func<T> action)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}

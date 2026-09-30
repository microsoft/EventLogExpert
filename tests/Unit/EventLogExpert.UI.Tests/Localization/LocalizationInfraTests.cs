// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Localization.Plural;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.ActivityCorrelation;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.ResolutionCoverage;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.FilterEditor.Comparison;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Globalization;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.Localization;

[Collection(CultureSensitiveCollection.Name)]
public sealed class LocalizationInfraTests
{
    private static readonly Regex s_countOneComparison = new(
        @"==\s*1\b|\b1\s*==|!=\s*1\b|\b1\s*!=|\bis\s+not\s+1\b|\bis\s+1\b|\b1\s*=>",
        RegexOptions.Compiled);

    private static readonly Regex s_icuPluralPattern = new(@"\{\s*[A-Za-z0-9_]+\s*,\s*plural\s*,", RegexOptions.Compiled);

    private static readonly Regex s_legacyCamelCasePluralWord = new(@"[a-z0-9](One|Many)(?=[_A-Z]|$)", RegexOptions.Compiled);

    private static readonly Regex s_legacyFourWayPluralKey = new(@"_Item\d+_Dup\d+$|_Accept_\d\d$", RegexOptions.Compiled);

    private static readonly Regex s_legacyTerminalPluralSegment = new(@"_(One|Many)(?=_|$)", RegexOptions.Compiled);

    private static readonly string[] s_pluralQuantityArguments =
    [
        "count",
        "total",
        "databaseCount",
        "duplicateCount",
        "itemCount",
        "filterCount",
        "logCount",
        "memberCount",
        "shownCount",
        "upgradeCount",
        "shown",
        "added",
        "replaced",
        "skipped",
        "ambiguous",
        "eligible",
        "updatedTags",
        "cancelled",
        "distinctCount",
        "filtered",
        "loadingCount",
        "selected",
        "unresolved",
        "errorCritical"
    ];

    // Non-quantity placeholders rendered inside plural branch bodies: percentages and strings only.
    private static readonly string[] s_pluralRenderedPlaceholderAllowlist =
    [
        "percent",
        "name",
        "names",
        "entryName",
        "entryNames",
        "fileName",
        "fileNames",
        "path",
        "tag",
        "newTag",
        "oldTag",
        "mergeTargetTag",
        "reason",
        "upgradingNames"
    ];

    private static readonly Regex s_quotedIdentifierLiteral = new(@"""([A-Za-z0-9_]+)""", RegexOptions.Compiled);

    private enum RenderShape
    {
        // render(count=1) and render(count=2) differ after number-normalization: a genuine one/other split.
        SingularPluralDiffer,

        // render(count=1) and render(count=2) are identical after number-normalization: an other-only pattern, or a
        // pattern that authors both branches but renders them the same (a noun that does not inflect).
        UniformAcrossCount
    }

    [Fact]
    public void ColumnDisplayValues_MirrorToFullString()
    {
        var neutralValues = ResxValues();

        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            var key = $"Column_{column}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(column.ToFullString(), neutral);
        }
    }

    [Fact]
    public void DatabaseToolsLocalizableTextArgCounts_MatchResxPlaceholderArity()
    {
        var constants = DatabaseToolsKeyConstants();
        var neutralValues = ResxValues();
        var checkedSites = 0;

        foreach (string source in LocalizationSourceScan.EnumerateProductionSource().Select(File.ReadAllText))
        {
            foreach (string call in ExtractMethodCalls(source, "LocalizableText"))
            {
                var parts = SplitArguments(call);

                if (parts.Count != 2) { continue; }
                if (!TryResolveDatabaseToolsKey(parts[0], constants, out string? key)) { continue; }

                var argumentCount = CountLocalizableTextArguments(parts[1]);
                if (argumentCount < 0) { continue; }

                Assert.True(neutralValues.TryGetValue(key!, out string? value), $"Key {key} is missing from the neutral resx.");
                Assert.Equal(PlaceholderArity(value!), argumentCount);
                checkedSites++;
            }
        }

        Assert.True(checkedSites >= 50, $"Arg-arity guard only checked {checkedSites} LocalizableText sites.");
    }

    [Fact]
    public void DatabaseToolsLocalizableTextKeyUsages_HasExpectedFloor()
    {
        var examined = 0;

        foreach (string source in LocalizationSourceScan.EnumerateProductionSource().Select(File.ReadAllText))
        {
            examined += Regex.Matches(source, @"new\s+LocalizableText\s*\(\s*DatabaseToolsLogKeys\.").Count;
        }

        Assert.True(examined >= 60, $"DatabaseTools localizable key usage guard examined only {examined} sites.");
    }

    [Fact]
    public void DatabaseToolsLogKeys_MatchNeutralResxAndAvoidCultureFormatSpecifiers()
    {
        var keyValues = typeof(DatabaseToolsLogKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var resxValues = ResxValues()
            .Where(entry => entry.Key.StartsWith("DatabaseTools_Op_", StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(keyValues, resxValues.Select(entry => entry.Key));

        var cultureFormatValues = resxValues
            .Where(entry => Regex.IsMatch(entry.Value, @"\{\d+:[^}]+\}"))
            .Select(entry => entry.Key)
            .ToList();

        Assert.Empty(cultureFormatValues);
    }

    [Fact]
    public void DatabaseToolsSchemaMessageValues_MirrorSchemaStateMessages()
    {
        var neutralValues = ResxValues();

        Assert.Equal(
            SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.DefaultLabel, "{0}"),
            neutralValues[DatabaseToolsLogKeys.SchemaUnrecognizedDefault]);
        Assert.Equal(
            SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.SourceLabel, "{0}"),
            neutralValues[DatabaseToolsLogKeys.SchemaUnrecognizedSource]);
        Assert.Equal(
            SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.TargetLabel, "{0}"),
            neutralValues[DatabaseToolsLogKeys.SchemaUnrecognizedTarget]);
        Assert.Equal(2, PlaceholderArity(neutralValues[DatabaseToolsLogKeys.UpgradeUnsupportedV1OrV2Schema]));
    }

    [Fact]
    public void DebugLogPlaceholderValues_HaveExpectedPlaceholderArity()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("DebugLog_Filter_EditAria", 1),
            ("DebugLog_Filter_RemoveRowAria", 1)
        ];

        foreach ((string key, int arity) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? value), $"Missing neutral RESX value for {key}.");
            Assert.Equal(arity, PlaceholderArity(value));
        }

        // Completeness: these are the ONLY DebugLog_* keys that carry placeholders. A new placeholder-bearing DebugLog
        // key must extend this list (so its arity stays guarded); arity-0 keys and value edits do not touch this.
        Assert.Equal(
            expected.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal),
            neutralValues
                .Where(pair => pair.Key.StartsWith("DebugLog_", StringComparison.Ordinal) && PlaceholderArity(pair.Value) > 0)
                .Select(pair => pair.Key)
                .OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void EnumMappedKeyFamilies_MatchEnumMembersExactly()
    {
        // Bidirectional per family: an authored key with no enum member is an orphan/typo; an enum member with no
        // key echoes its key name at runtime (Localizer[$"{stem}{member}"]). The two-rule orphan guard cannot see
        // either case because the family's interpolation site keeps the whole stem "referenced".
        (string Stem, Type EnumType)[] families =
        [
            ("Settings_Theme_", typeof(Theme)),
            ("Settings_CopyFormat_", typeof(EventCopyFormat)),
            ("Settings_LogLevel_", typeof(LogLevel)),
            ("Column_", typeof(ColumnName)),
            ("Dashboard_Group_", typeof(ScenarioGroup)),
            ("Dashboard_Presence_", typeof(ChannelPresence)),
            ("Dashboard_Enablement_", typeof(ChannelEnablement)),
            ("Details_Placeholder_", typeof(PlaceholderKind)),
            ("Details_Property_", typeof(DetailsPropertyLabel)),
            ("Db_Status_", typeof(DatabaseStatus)),
            ("Db_StatusToken_", typeof(DatabaseStatus)),
            ("Db_UpgradePhase_", typeof(UpgradePhase)),
            ("Explain_", typeof(GlossaryTerm)),
            ("ResolutionStatus_", typeof(EventResolutionStatus)),
            ("Correlation_Role_", typeof(ActivityNodeRole)),
            ("Coverage_Status_", typeof(CoverageStatus)),
            ("Severity_Level_", typeof(SeverityLevel)),
            ("Histogram_Dimension_", typeof(HistogramDimension)),
            ("Histogram_HighlightColor_", typeof(HighlightColor)),
            ("Histogram_Severity_", typeof(HistogramSeverityBucket)),
            ("Stats_Dimension_", typeof(StatsDimension)),
            ("StatusBar_Memory_Value_", typeof(MemoryUsageLevel)),
            ("StatusBar_Memory_Announce_", typeof(MemoryUsageLevel)),
            ("StatusBar_Memory_Tooltip_", typeof(MemoryUsageLevel)),
            ("FilterLens_Property_", typeof(EventProperty)),
            ("LibraryTab_", typeof(LibraryTab)),
            ("FilterEditor_Comparison_", typeof(ComparisonOperatorSelect.ComparisonKind))
        ];

        var resxKeys = ResxKeys();

        foreach (var (stem, enumType) in families)
        {
            var expected = Enum.GetNames(enumType)
                .Select(name => stem + name)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            var actual = resxKeys
                .Where(key => key.StartsWith(stem, StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(expected, actual);
        }

        Assert.Contains("Severity_Unknown", resxKeys);
    }

    [Fact]
    public void EveryProductionLiteralKeyReference_ExistsInNeutralResx()
    {
        // Extract every identifier-like string literal INSIDE a Localizer[...] indexer or a .GetString(...) call -
        // including keys selected by a conditional such as Localizer[count == 1 ? "..._One" : "..._Many", count] and
        // composite keys such as Localizer["...", arg] - so a key referenced only through a ternary cannot silently drift
        // out of the RESX. Format arguments are variables or punctuation separators (", ", " "), never identifier-like
        // literals, so scanning the whole call span never misreads an argument as a key.
        var localizerCallPattern = new Regex(
            @"[Ll]ocalizer\[([^\]]*)\]|\.GetString\(([^)]*)\)",
            RegexOptions.Compiled);
        var keyLiteralPattern = new Regex(@"""([A-Za-z0-9_]+)""", RegexOptions.Compiled);

        var sources = LocalizationSourceScan.EnumerateProductionSource()
            .Select(File.ReadAllText)
            .ToList();

        var referenced = sources
            .SelectMany(source => localizerCallPattern.Matches(source))
            .SelectMany(call => keyLiteralPattern.Matches(call.Groups[1].Value + call.Groups[2].Value))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var authored = ResxKeys().ToHashSet(StringComparer.Ordinal);
        var missing = referenced.Where(key => !authored.Contains(key)).ToList();

        Assert.True(referenced.Count >= 150, $"Production literal localizer scan found only {referenced.Count} keys.");
        Assert.True(missing.Count == 0, $"Production literal localizer keys missing from RESX: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryResourceKey_IsReferencedInProductionSource()
    {
        // A key is referenced if its literal "{key}" appears in source, OR it belongs to a documented dynamic
        // family whose interpolation site ($"{stem}...) is present - so deleting that site re-surfaces the whole
        // family as orphaned. obj/bin are excluded (via LocalizationSourceScan) so a stale generated .g.cs can
        // never keep a truly-orphaned key green. Individual dynamic-member orphans are caught by the enum cross-check below.
        string[] dynamicStems = ["Dashboard_Group_", "Settings_CopyFormat_", "Settings_LogLevel_", "Settings_Theme_"];

        var source = string.Join("\n", LocalizationSourceScan.EnumerateProductionSource().Select(File.ReadAllText));

        var orphans = ResxKeys()
            .Where(key =>
                !source.Contains($"\"{key}\"", StringComparison.Ordinal) &&
                !dynamicStems.Any(stem =>
                    key.StartsWith(stem, StringComparison.Ordinal) &&
                    source.Contains($"$\"{stem}", StringComparison.Ordinal)))
            .ToList();

        Assert.True(orphans.Count == 0, $"Authored-but-unreferenced RESX keys: {string.Join(", ", orphans)}");
    }

    [Theory]
    [InlineData("N0", true)]
    [InlineData("n0", true)]
    [InlineData("N2", false)]
    [InlineData("N", false)]
    [InlineData(" N0", false)]
    [InlineData("N0 ", false)]
    [InlineData("0,", false)]
    [InlineData("#,0", false)]
    [InlineData("D", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void ExactGroupedIntegerFormatPredicate_AcceptsOnlyN0(string? format, bool expected) =>
        Assert.Equal(expected, IsExactGroupedIntegerFormat(format));

    [Fact]
    public void FilePickerNeutralValues_HaveExpectedPlaceholderArity()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("FilePicker_Error_FirstExtensionBlank", 0),
            ("FilePicker_Error_NoExtensions", 0),
            ("FilePicker_Error_NoFileTypeChoice", 0),
            ("FilePicker_Filter_AllFiles", 0),
            ("FilePicker_Filter_SupportedTypes", 1),
            ("FilePicker_Title_OpenEventLogs", 0),
            ("FilePicker_Title_SaveAs", 0),
            ("FilePicker_Title_SelectFolder", 0)
        ];

        foreach ((string key, int arity) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(arity, PlaceholderArity(neutral));
        }

        Assert.Equal(
            expected.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal),
            neutralValues.Keys.Where(key => key.StartsWith("FilePicker_", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void LegacyPluralDetectors_FlagRetiredSpellingsButSpareLegitimateNames()
    {
        // Positive controls: the NoLegacyPluralMechanismsRemain guard is only meaningful if these retired spellings
        // actually trip its detectors. Terminal "_One"/"_Many", CamelCase One/Many words (even before a later suffix),
        // the mashed "OneMany", terminal CamelCase, and the four-way "_ItemN_DupN"/"_Accept_NN" forms must all flag.
        foreach (string offender in new[]
        {
            "Banner_Export_Complete_One", "Banner_Export_Complete_Many", "WidgetOne", "WidgetMany",
            "TagOne_Ambiguous", "ItemOne_DupMany", "Accept_OneMany", "Filter_Item1_Dup2", "Filter_Accept_12",
            "Update_Alert_One_Message", "Update_Alert_Many_Message"
        })
        {
            Assert.True(IsLegacyPluralKeyName(offender), $"Expected '{offender}' to be flagged as a legacy plural key name.");
        }

        // Negative controls: real keys and ordinary words that merely contain the substrings must be spared, or the
        // guard would false-positive and force churn on unrelated names.
        foreach (string legitimate in new[]
        {
            "StatusBar_Loading_ManyLogs", "StatusBar_Counts_Total", "Db_Badge_None", "Details_Property_Money",
            "Explain_Zone", "Correlation_Component_Ready", "Settings_Phone_Number"
        })
        {
            Assert.False(IsLegacyPluralKeyName(legitimate), $"Expected '{legitimate}' to be spared by the legacy plural key guard.");
        }

        // The count-driven selection predicate must catch each retired comparison spelling (==, reversed, !=, is,
        // switch arm, precomputed local) paired with a One/Many key.
        foreach (string selection in new[]
        {
            "Localizer[count == 1 ? \"Foo_One\" : \"Foo_Many\"]",
            "Localizer[1 == count ? \"Foo_One\" : \"Foo_Many\"]",
            "Localizer[count != 1 ? \"Foo_Many\" : \"Foo_One\"]",
            "Localizer[count is 1 ? \"FooOne\" : \"FooMany\"]",
            "var key = count == 1 ? \"Foo_One\" : \"Foo_Many\";",
            "count switch { 1 => \"Foo_One\", _ => \"Foo_Many\" }"
        })
        {
            Assert.True(IsLegacyCountDrivenPluralSelection(selection), $"Expected '{selection}' to be flagged as a count-driven plural selection.");
        }

        // ...but a count comparison without a One/Many key, a One/Many key without a count comparison, and an unrelated
        // method named "SelectMany"/"WaitOne" must all be spared.
        foreach (string benign in new[]
        {
            "Localizer[\"StatusBar_Counts_Total\", total]",
            "Localizer[isActive ? \"Filter_On\" : \"Filter_Off\"]",
            "if (count == 1) { LoadSingle(); }",
            "entries.SelectMany(e => e.Tags).Count == 1"
        })
        {
            Assert.False(IsLegacyCountDrivenPluralSelection(benign), $"Expected '{benign}' to be spared by the count-driven plural selection guard.");
        }

        // Exercise the whole source scanner (not just the predicate): a precomputed-local offender in synthetic source
        // must be reported even though the retired ternary and the eventual Localizer[key] call are separate
        // statements, while a clean source (a proper PluralText.Format plus an unrelated SelectMany) yields nothing.
        Assert.NotEmpty(ScanForLegacyCountDrivenSelections(
            "void M(int count) {\n    var key = count == 1 ? \"Foo_One\" : \"Foo_Many\";\n    _ = Localizer[key, count];\n}",
            "Synthetic.cs"));
        Assert.Empty(ScanForLegacyCountDrivenSelections(
            "void M(int count) {\n    _ = PluralText.Format(Localizer, \"Foo\", (\"count\", count));\n    var many = items.SelectMany(x => x.Tags).Count;\n}",
            "Synthetic.cs"));
    }

    [Fact]
    public void Localizer_KnownKey_Resolves()
    {
        var key = "FindBar_NoResults";
        var result = BuildLocalizer()[key];

        Assert.False(result.ResourceNotFound);
        Assert.NotEqual(key, result.Value);
    }

    [Fact]
    public void Localizer_MissingKey_ReportsNotFoundAndEchoesKey()
    {
        var result = BuildLocalizer()["FindBar_ThisKeyDoesNotExist"];

        Assert.True(result.ResourceNotFound);
        Assert.Equal("FindBar_ThisKeyDoesNotExist", result.Value);
    }

    [Fact]
    public void MenuAlertValues_HaveExpectedPlaceholderArity()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("Menu_Alert_OpenFileFailed_Title", 0),
            ("Menu_Alert_OpenFolderFailed_Title", 0),
            ("Menu_Alert_OpenFolderFailed_Message", 1),
            ("Menu_Alert_LaunchBrowserFailed_Title", 0),
            ("Menu_Alert_LogElevationRequired_Title", 0),
            ("Menu_Alert_LogElevationRequired_Message", 0),
            ("Menu_Alert_OpenLogFailed_Title", 0),
            ("Menu_Alert_LogNotFound_Message", 1),
            ("Menu_Alert_OpenLogError_Message", 1)
        ];

        foreach ((string key, int arity) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? value), $"Missing neutral RESX value for {key}.");
            Assert.Equal(arity, PlaceholderArity(value));
        }

        Assert.Equal(
            expected.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal),
            neutralValues.Keys.Where(key => key.StartsWith("Menu_Alert_", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void NeutralBannerValues_HaveExpectedPlaceholderArity()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("Banner_Attention_DismissAria", 0),
            ("Banner_Attention_ErrorTitle", 0),
            ("Banner_Attention", 0),
            ("Banner_Attention_OpenDatabases", 0),
            ("Banner_Attention_OpenFailed", 0),
            ("Banner_Attention_OpenFailedDetail", 1),
            ("Banner_Critical_Copied", 0),
            ("Banner_Critical_CopyDetails", 0),
            ("Banner_Critical_RecoveryFailed", 1),
            ("Banner_Critical_Relaunch", 0),
            ("Banner_Critical_Reload", 0),
            ("Banner_Critical_RestartFailed", 0),
            ("Banner_Critical_Unexpected", 2),
            ("Banner_Db_Import_Failed_Message", 1),
            ("Banner_Db_Import_Failed_Title", 0),
            ("Banner_Db_Import_FailurePart", 2),
            ("Banner_Db_Import_FailureSummary", 1),
            ("Banner_Db_Import_None", 0),
            ("Banner_Db_Import_Partial", 0),
            ("Banner_Db_Import_Partial_Message", 2),
            ("Banner_Db_Import_Partial_Title", 0),
            ("Banner_Db_Import_Success", 0),
            ("Banner_Db_Import_Success_Title", 0),
            ("Banner_Db_Import_UpgradeFailurePart", 2),
            ("Banner_Db_OperationFailed_Message", 2),
            ("Banner_Db_OperationNoun_Import", 0),
            ("Banner_Db_OperationNoun_Toggle", 1),
            ("Banner_Db_OperationNoun_UpgradeBatch", 0),
            ("Banner_Db_OperationNoun_UpgradeSingle", 1),
            ("Banner_Db_RemoveFailed_Message", 2),
            ("Banner_Db_RemoveFailed_Title", 0),
            ("Banner_Db_UpdateFailed_Title", 0),
            ("Banner_Db_UpgradeFailed_Message", 2),
            ("Banner_Db_UpgradeFailed_Title", 0),
            ("Banner_EmptyLog", 0),
            ("Banner_EmptyLog_Title", 0),
            ("Banner_Error_DismissAria", 0),
            ("Banner_Export_Blocked_Faulted", 0),
            ("Banner_Export_Blocked_InProgress", 0),
            ("Banner_Export_Blocked_NoColumns", 0),
            ("Banner_Export_Blocked_NoEvents", 0),
            ("Banner_Export_Blocked_Updating", 0),
            ("Banner_Export_Canceled_Message", 0),
            ("Banner_Export_Canceled_Title", 0),
            ("Banner_Export_Complete", 0),
            ("Banner_Export_Complete_Title", 0),
            ("Banner_Export_Failed_Title", 0),
            ("Banner_Export_Progress", 0),
            ("Banner_Export_Title", 0),
            ("Banner_Filter_AddToSetFailed_Message", 0),
            ("Banner_Filter_AddToSetFailed_Title", 0),
            ("Banner_Filter_CreateFailed_Message", 1),
            ("Banner_Filter_CreateFailed_Title", 0),
            ("Banner_Filter_DeleteFailed_Message", 0),
            ("Banner_Filter_DeleteFailed_Title", 0),
            ("Banner_Filter_EntrySaveFailed_Message", 1),
            ("Banner_Filter_EntrySaveFailed_Title", 0),
            ("Banner_Filter_EntryTagsFailed_Message", 0),
            ("Banner_Filter_EntryTagsFailed_Title", 0),
            ("Banner_Filter_EntryUpdateFailed_Message", 1),
            ("Banner_Filter_EntryUpdateFailed_Title", 0),
            ("Banner_Filter_FavoriteFailed_Message", 0),
            ("Banner_Filter_FavoriteFailed_Title", 0),
            ("Banner_Filter_ImportFailed_Message", 0),
            ("Banner_Filter_ImportFailed_Title", 0),
            ("Banner_Filter_NotLoaded_Message", 0),
            ("Banner_Filter_NotLoaded_Title", 0),
            ("Banner_Filter_PromoteFailed_Message", 0),
            ("Banner_Filter_PromoteFailed_Title", 0),
            ("Banner_Filter_RenameFailed_Message", 0),
            ("Banner_Filter_RenameFailed_Title", 0),
            ("Banner_Filter_SaveFailed_Message", 1),
            ("Banner_Filter_SaveFailed_Title", 0),
            ("Banner_Filter_SetUpdateFailed_Message", 0),
            ("Banner_Filter_SetUpdateFailed_Title", 0),
            ("Banner_Filter_TagBulkFailed_Message", 0),
            ("Banner_Filter_TagBulkFailed_Title", 0),
            ("Banner_Info_DismissAria", 0),
            ("Banner_List_Separator", 0),
            ("Banner_Nav_NextAria", 0),
            ("Banner_Nav_PreviousAria", 0),
            ("Banner_Pagination", 2),
            ("Banner_Recovery_Failed_Delete", 1),
            ("Banner_Recovery_Failed_Restore", 1),
            ("Banner_Recovery_Failed_Title", 0),
            ("Banner_Recovery_Needed", 0),
            ("Banner_Recovery_Needed_Title", 0),
            ("Banner_Recovery_Resolve", 0),
            ("Banner_Upgrade_InProgress", 4),
            ("Banner_Upgrade_Preparing", 0),
            ("Banner_Upgrade_QueuedBatches", 0)
        ];

        foreach ((string key, int arity) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? value), $"Missing neutral RESX value for {key}.");
            Assert.Equal(arity, PlaceholderArity(value));
        }

        Assert.Equal(
            expected.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal),
            neutralValues.Keys.Where(key => key.StartsWith("Banner_", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void NeutralCoverageStatusValues_MirrorLabels()
    {
        // The status cell localizes CoverageStatus while the TSV/copy path renders CoverageStatusText.Label. This pins
        // each neutral Coverage_Status_* value equal to that invariant label so the on-screen pill and the copied table
        // never disagree. (No severity counterpart: SeverityLevel has no invariant twin consumer.)
        var neutralValues = ResxValues();

        foreach (CoverageStatus status in Enum.GetValues<CoverageStatus>())
        {
            var key = $"Coverage_Status_{status}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(CoverageStatusText.Label(status), neutral);
        }
    }

    [Fact]
    public void NeutralFilterImportValues_HaveExpectedPlaceholderArity()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("FilterImport_Action_ImportAsIs", 0),
            ("FilterImport_Action_Normalize", 0),
            ("FilterImport_Added", 0),
            ("FilterImport_Ambiguous", 0),
            ("FilterImport_Announcement_TagRemoved", 0),
            ("FilterImport_Announcement_TagRenamed", 0),
            ("FilterImport_BlockedHeader", 0),
            ("FilterImport_EmptyValueMessage", 0),
            ("FilterImport_EmptyValueTitle", 0),
            ("FilterImport_Error_EmptyFile", 0),
            ("FilterImport_Error_EntryIdExpectedJsonString", 1),
            ("FilterImport_Error_EntryIdExpectedNonEmptyString", 0),
            ("FilterImport_Error_EntryIdInvalidGuid", 1),
            ("FilterImport_Error_InvalidBasicFilters", 0),
            ("FilterImport_Error_InvalidSchemaVersion", 1),
            ("FilterImport_Error_MissingEntriesProperty", 0),
            ("FilterImport_Error_MissingEntryName", 0),
            ("FilterImport_Error_NormalizedBasicFilterFormatFailed", 1),
            ("FilterImport_Error_NormalizedBasicFilterRebuildFailed", 1),
            ("FilterImport_Error_SchemaVersionNotInteger", 0),
            ("FilterImport_Error_TagsExpectedArrayOrNull", 1),
            ("FilterImport_Error_TagsExpectedStringElement", 1),
            ("FilterImport_Error_TagsUnexpectedEnd", 0),
            ("FilterImport_Error_UnknownLibraryEntryKind", 1),
            ("FilterImport_Error_UnsupportedSchemaVersion", 1),
            ("FilterImport_Error_UnsupportedShape", 0),
            ("FilterImport_KeepEmpty", 0),
            ("FilterImport_MoreNames", 1),
            ("FilterImport_NothingToImport", 0),
            ("FilterImport_Overwrite", 0),
            ("FilterImport_OverwriteNamesHeader", 0),
            ("FilterImport_PreviewHeader", 0),
            ("FilterImport_RemovedEmptyNotice", 0),
            ("FilterImport_Renames", 0),
            ("FilterImport_Skipped", 0),
            ("FilterImport_Summary_Tag", 0),
            ("FilterImport_Summary_Tag_Ambiguous", 0),
            ("FilterImport_TagUpdates", 0),
            ("LibraryTab_Favorites", 0),
            ("LibraryTab_PreviouslyUsed", 0),
            ("LibraryTab_Saved", 0)
        ];

        foreach ((string key, int arity) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? value), $"Missing neutral RESX value for {key}.");
            Assert.Equal(arity, PlaceholderArity(value));
        }

        Assert.Equal(
            expected.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal),
            neutralValues.Keys
                .Where(key => key.StartsWith("FilterImport_", StringComparison.Ordinal) ||
                    key.StartsWith("LibraryTab_", StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void NeutralFilterLensPropertyValues_MirrorToFullString()
    {
        // The UI FilterLensLabelFormatter localizes the FilterLensLabel.PropertyComparison property name; the invariant
        // baseline (FilterLensLabelText.Invariant) and the filter value picker both render property.ToFullString(). This
        // pins each neutral FilterLens_Property_* value equal to that canonical string (read straight from the neutral
        // RESX) so the chip, the picker, and the invariant announcement never disagree.
        var neutralValues = ResxValues();

        foreach (EventProperty property in Enum.GetValues<EventProperty>())
        {
            var key = $"FilterLens_Property_{property}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(property.ToFullString(), neutral);
        }
    }

    [Fact]
    public void NeutralHistogramCountsRenderGroupedQuantities()
    {
        IStringLocalizer<SharedResource> localizer = BuildLocalizer();

        string formatted = HistogramGroupLabelFormatter.Format(
            localizer,
            new HistogramGroupLabel.CategoricalOther(HistogramDimension.Source, 1200));

        Assert.Contains(1200.ToString("N0", CultureInfo.CurrentCulture), formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void NeutralHistogramDimensionValues_MirrorPreviousDisplayText()
    {
        var neutralValues = ResxValues();
        (HistogramDimension Dimension, string Text)[] expected =
        [
            (HistogramDimension.Severity, "Severity"),
            (HistogramDimension.Source, "Source"),
            (HistogramDimension.EventId, "Event ID"),
            (HistogramDimension.TaskCategory, "Task Category"),
            (HistogramDimension.Opcode, "Opcode"),
            (HistogramDimension.Log, "Log"),
            (HistogramDimension.LogonType, "Logon Type"),
            (HistogramDimension.TicketEncryptionType, "Ticket Encryption Type"),
            (HistogramDimension.ErrorCode, "Error Code"),
            (HistogramDimension.ProcessImage, "Process Image"),
            (HistogramDimension.ParentProcessImage, "Parent Process Image")
        ];

        foreach ((HistogramDimension dimension, string text) in expected)
        {
            Assert.Equal(text, neutralValues[$"Histogram_Dimension_{dimension}"]);
        }
    }

    [Fact]
    public void NeutralHistogramSpaceBearingValues_PreserveSeparatorAndBranchPrefixes()
    {
        var neutralValues = ResxValues();

        Assert.Equal(", ", neutralValues["Histogram_Breakdown_Separator"]);

        // ErrorCritical authors both one/other branches, but they are intentionally identical (the noun does not
        // inflect), so its shape is uniform across counts; TopSources genuinely inflects. Both keep the " - " prefix.
        AssertPluralPatternBehavior(
            neutralValues,
            "Stats_Headline_ErrorCritical",
            "errorCritical",
            [Grouped("errorCritical")],
            ["one", "other"],
            RenderShape.UniformAcrossCount,
            expectedPrefix: " - ");
        AssertPluralPatternBehavior(
            neutralValues,
            "Stats_Headline_TopSources",
            "count",
            [Grouped("count"), Raw("percent")],
            ["one", "other"],
            RenderShape.SingularPluralDiffer,
            expectedPrefix: " - ");
    }

    [Fact]
    public void NeutralHistogramSummaryTemplates_KeepGeneralShortDateFormat()
    {
        var neutralValues = ResxValues();
        string[] keys =
        [
            "Histogram_RegionAria",
            "Histogram_RegionAria_Breakdown",
            "Histogram_WindowAnnouncement",
            "Histogram_WindowAnnouncement_Breakdown"
        ];

        foreach (string key in keys)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? value), $"Missing neutral RESX value for {key}.");
            Assert.Contains("{2:g}", value, StringComparison.Ordinal);
            Assert.Contains("{3:g}", value, StringComparison.Ordinal);
        }

        Assert.Equal(4, PlaceholderArity(neutralValues["Histogram_RegionAria"]));
        Assert.Equal(5, PlaceholderArity(neutralValues["Histogram_RegionAria_Breakdown"]));
        Assert.Equal(4, PlaceholderArity(neutralValues["Histogram_WindowAnnouncement"]));
        Assert.Equal(5, PlaceholderArity(neutralValues["Histogram_WindowAnnouncement_Breakdown"]));
    }

    [Fact]
    public void NeutralPropertyLabelValues_MirrorInvariant()
    {
        // The formatter emits the typed DetailsPropertyLabel; copy renders it via DetailsPropertyText.Invariant. This
        // pins each neutral Details_Property_* value equal to that invariant (read straight from the neutral RESX, so a
        // shipped translation under an ambient culture cannot red it), mirroring the ScenarioGroup drift guard.
        var neutralValues = ResxValues();

        foreach (DetailsPropertyLabel label in Enum.GetValues<DetailsPropertyLabel>())
        {
            var key = $"Details_Property_{label}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(DetailsPropertyText.Invariant(label), neutral);
        }
    }

    [Fact]
    public void NeutralProviderDatabaseValues_HaveExpectedPlaceholderArity()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("Db_Badge_RecoveryRequired", 0),
            ("Db_Entry_Aria_Restore", 1),
            ("Db_Entry_Aria_RetryClassification", 1),
            ("Db_Entry_Aria_RetryUpgrade", 1),
            ("Db_Entry_Aria_Select", 1),
            ("Db_Entry_Aria_Upgrade", 1),
            ("Db_Entry_MixedOs_Capped", 0),
            ("Db_Entry_MixedOs_Count", 0),
            ("Db_Entry_PendingToggle", 0),
            ("Db_Entry_PhaseProgress", 3),
            ("Db_Entry_ProgressAria", 2),
            ("Db_Entry_Remove", 0),
            ("Db_Entry_Restore", 0),
            ("Db_Entry_RetryClassification", 0),
            ("Db_Entry_RetryUpgrade", 0),
            ("Db_Entry_SourceOs", 1),
            ("Db_Entry_UnknownOs", 0),
            ("Db_Entry_Upgrade", 0),
            ("Db_Entry_Upgrading", 0),
            ("Db_Fail_CancellationRollbackFailed", 1),
            ("Db_Fail_CannotUpgradeStatus", 1),
            ("Db_Fail_EntryNotFound", 0),
            ("Db_Fail_ImportOpenArchiveFailed", 1),
            ("Db_Fail_MigrationRollbackFailed", 2),
            ("Db_Fail_RecoveryRequiredBakAlreadyPresent", 0),
            ("Db_Fail_RecoveryRequiredBakAppearedDuringBackup", 0),
            ("Db_Fail_RecoveryRequiredBackupExists", 0),
            ("Db_Fail_RecoveryRequiredResolveFirst", 0),
            ("Db_Fail_UpgradeCleanupFailed", 0),
            ("Db_Fail_UpgradeVerificationFailed", 0),
            ("Db_Fail_VerificationOrCleanupRollbackFailed", 1),
            ("Db_Manage_Upgrade_Cancelled", 0),
            ("Db_Manage_Upgrade_MultipleFailure", 5),
            ("Db_Manage_Upgrade_SingleFailure", 2),
            ("Db_Manage_Upgrade_Success", 0),
            ("Db_Picker_ImportPrompt", 0),
            ("DatabaseRecoveryModal_Delete", 0),
            ("DatabaseRecoveryModal_DeleteAll", 0),
            ("DatabaseRecoveryModal_Description", 0),
            ("DatabaseRecoveryModal_Explanation", 0),
            ("DatabaseRecoveryModal_Restore", 0),
            ("DatabaseRecoveryModal_RestoreAll", 0),
            ("Modal_Apply", 0)
        ];

        foreach ((string key, int arity) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? value), $"Missing neutral RESX value for {key}.");
            Assert.Equal(arity, PlaceholderArity(value));
        }

        Assert.Equal(7, neutralValues.Keys.Count(key => key.StartsWith("Db_Status_", StringComparison.Ordinal)));
        Assert.Equal(3, neutralValues.Keys.Count(key => key.StartsWith("Db_UpgradePhase_", StringComparison.Ordinal)));
        Assert.Equal(12, neutralValues.Keys.Count(key => key.StartsWith("Db_Fail_", StringComparison.Ordinal)));
        Assert.Equal(7, neutralValues.Keys.Count(key => key.StartsWith("Db_StatusToken_", StringComparison.Ordinal)));
        Assert.Equal(4, neutralValues.Keys.Count(key => key.StartsWith("Db_Manage_Upgrade_", StringComparison.Ordinal)));
    }

    [Fact]
    public void NeutralResolutionStatusValues_MirrorTokens()
    {
        // The status display localizes EventResolutionStatus while copy/filter/storage use the invariant token. This
        // pins each neutral ResolutionStatus_* value equal to ResolutionStatusTokens.Format so display and token agree.
        var neutralValues = ResxValues();

        foreach (EventResolutionStatus status in Enum.GetValues<EventResolutionStatus>())
        {
            var key = $"ResolutionStatus_{status}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(ResolutionStatusTokens.Format(status), neutral);
        }
    }

    [Fact]
    public void NeutralStatsDimensionValues_MirrorPreviousDisplayText()
    {
        var neutralValues = ResxValues();
        (StatsDimension Dimension, string Text)[] expected =
        [
            (StatsDimension.Source, "Source"),
            (StatsDimension.EventId, "Event ID"),
            (StatsDimension.TaskCategory, "Task Category"),
            (StatsDimension.User, "User")
        ];

        foreach ((StatsDimension dimension, string text) in expected)
        {
            Assert.Equal(text, neutralValues[$"Stats_Dimension_{dimension}"]);
        }
    }

    [Fact]
    public void NeutralStatusBarSpaceBearingValues_KeepXmlSpaceAndExactWhitespace()
    {
        XNamespace xmlNamespace = XNamespace.Xml;
        var data = XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!
            .Elements("data")
            .Where(element => ((string?)element.Attribute("name"))?.StartsWith("StatusBar_", StringComparison.Ordinal) == true)
            .ToDictionary(element => (string)element.Attribute("name")!, StringComparer.Ordinal);

        string[] spaceBearingKeys =
        [
            "StatusBar_Counts_TotalSelected",
            "StatusBar_Counts_ShownOfTotalSelected",
            "StatusBar_Memory_Value_Elevated",
            "StatusBar_Memory_Value_High"
        ];

        foreach (string key in spaceBearingKeys)
        {
            Assert.Equal("preserve", data[key].Attribute(xmlNamespace + "space")?.Value);
        }
    }

    [Fact]
    public void NeutralStatusBarValues_HaveExpectedPlaceholderArityAndPluralBehavior()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("StatusBar_Source_None", 0),
            ("StatusBar_Source_AllLogs", 1),
            ("StatusBar_Source_Combined", 0),
            ("StatusBar_Source_CombinedCount", 0),
            ("StatusBar_Counts_Total", 0),
            ("StatusBar_Counts_TotalSelected", 0),
            ("StatusBar_Counts_ShownOfTotal", 2),
            ("StatusBar_Counts_ShownOfTotalSelected", 3),
            ("StatusBar_Coverage_Chip", 1),
            ("StatusBar_Coverage_AriaLabel", 1),
            ("StatusBar_Coverage_Tooltip", 0),
            ("StatusBar_Loading_Pending", 0),
            ("StatusBar_Loading_PendingPercent", 1),
            ("StatusBar_Loading_Count", 1),
            ("StatusBar_Loading_CountPercent", 2),
            ("StatusBar_Loading_ManyLogs", 0),
            ("StatusBar_Loading_Failed", 1),
            ("StatusBar_Memory_Value_Normal", 1),
            ("StatusBar_Memory_Value_Elevated", 1),
            ("StatusBar_Memory_Value_High", 1),
            ("StatusBar_Memory_Announce_Normal", 0),
            ("StatusBar_Memory_Announce_Elevated", 0),
            ("StatusBar_Memory_Announce_High", 0),
            ("StatusBar_Memory_Tooltip_Normal", 2),
            ("StatusBar_Memory_Tooltip_Elevated", 2),
            ("StatusBar_Memory_Tooltip_High", 2),
            ("StatusBar_Activity_Fault", 0),
            ("StatusBar_Activity_BufferFull", 0),
            ("StatusBar_Activity_Loading", 0),
            ("StatusBar_Activity_LoadingEvents", 0),
            ("StatusBar_Activity_Reordering", 0),
            ("StatusBar_Activity_ContinuouslyUpdating", 0),
            ("StatusBar_Resolver_FailedToOpen", 1),
            ("StatusBar_Resolver_NoResolver", 0),
            ("StatusBar_Resolver_FailedToLoad", 1),
            ("StatusBar_Filter_Chip", 0),
            ("StatusBar_Filter_Active", 0),
            ("StatusBar_Filter_Lens", 0),
            ("StatusBar_Filter_ActiveLens", 0),
            ("StatusBar_Stats_Show", 0),
            ("StatusBar_Stats_Hide", 0),
            ("StatusBar_NewEvents_Label", 1),
            ("StatusBar_NewEvents_None", 0),
            ("StatusBar_NewEvents_Load", 0)
        ];

        foreach ((string key, int arity) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? actual), $"Missing neutral RESX value for {key}.");
            Assert.Equal(arity, PlaceholderArity(actual));
        }

        (string Key, string Selector, PluralArgument[] Arguments, string[] Categories, RenderShape Shape)[] pluralExpected =
        [
            ("StatusBar_Source_CombinedCount", "memberCount", [Grouped("memberCount")], ["one", "other"], RenderShape.SingularPluralDiffer),
            ("StatusBar_Counts_Total", "total", [Grouped("total")], ["one", "other"], RenderShape.SingularPluralDiffer),
            ("StatusBar_Counts_TotalSelected", "total", [Grouped("total"), Grouped("selected")], ["other"], RenderShape.UniformAcrossCount),
            ("StatusBar_Coverage_Tooltip", "total", [Grouped("unresolved"), Grouped("total")], ["one", "other"], RenderShape.SingularPluralDiffer),
            ("StatusBar_Loading_ManyLogs", "loadingCount", [Grouped("loadingCount")], ["other"], RenderShape.UniformAcrossCount),
            ("StatusBar_Filter_Lens", "count", [Grouped("count")], ["one", "other"], RenderShape.SingularPluralDiffer),
            ("StatusBar_Filter_ActiveLens", "count", [Grouped("count")], ["one", "other"], RenderShape.SingularPluralDiffer)
        ];

        foreach ((string key, string selector, PluralArgument[] arguments, string[] categories, RenderShape shape) in pluralExpected)
        {
            AssertPluralPatternBehavior(neutralValues, key, selector, arguments, categories, shape);
        }

        Assert.Equal(
            expected.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal),
            neutralValues.Keys.Where(key => key.StartsWith("StatusBar_", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void NoLegacyPluralMechanismsRemain()
    {
        string deletedCountWrapperToken = "Localized" + "Count";
        IReadOnlyList<string> deletedWrapperReferences = FindTextOccurrences(
            EnumerateRepositoryTextFiles("src", "tests"),
            deletedCountWrapperToken);
        AssertNoOffenders(deletedWrapperReferences, "Deleted count-wrapper references remain");

        IReadOnlyList<string> legacyKeySelections = FindLegacyCountDrivenKeySelections();
        AssertNoOffenders(legacyKeySelections, "Count-driven singular/plural key selections remain");

        IReadOnlyList<string> legacyResourceKeys = FindLegacyPluralResourceKeys();
        AssertNoOffenders(legacyResourceKeys, "Legacy plural resource keys remain");

        IReadOnlyList<string> legacyDatabaseToolsConstants = typeof(DatabaseToolsLogKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (field.Name, Value: (string)field.GetRawConstantValue()!))
            .Where(field => IsLegacyPluralKeyName(field.Name) || IsLegacyPluralKeyName(field.Value))
            .Select(field => $"{field.Name} = {field.Value}")
            .OrderBy(offender => offender, StringComparer.Ordinal)
            .ToList();
        AssertNoOffenders(legacyDatabaseToolsConstants, "Legacy DatabaseTools plural key constants remain");
    }

    [Fact]
    public void PluralGuard_DetectsCompactPluralBareQuantityOffender()
    {
        string pattern = "{count,plural,one {{count:N0} x} other {{count} y}}";
        IReadOnlyList<string> offenders = PluralQuantityPlaceholderOffenders([new KeyValuePair<string, string>("Synthetic_Compact", pattern)]);

        string offender = Assert.Single(offenders);
        Assert.Contains("Synthetic_Compact", offender, StringComparison.Ordinal);
        Assert.Contains("quantity {count} must use :N0", offender, StringComparison.Ordinal);
    }

    [Fact]
    public void PluralQuantityPlaceholders_RenderGroupedNumericFormats()
    {
        HashSet<string> observedQuantityArguments = new(StringComparer.Ordinal);
        IReadOnlyList<string> offenders = PluralQuantityPlaceholderOffenders(
            ResxValues().Where(pair => s_icuPluralPattern.IsMatch(pair.Value)),
            observedQuantityArguments);

        Assert.Empty(offenders);
        Assert.Equal(
            s_pluralQuantityArguments.OrderBy(argument => argument, StringComparer.Ordinal),
            observedQuantityArguments.OrderBy(argument => argument, StringComparer.Ordinal));
    }

    [Fact]
    public void ProductionSource_NeverPinsThreadCulture()
    {
        // Matches pins (=, ??=; not ==/!=), not reads: only assignments would break OS-culture-following.
        var pinPattern = new Regex(
            @"(CurrentCulture|CurrentUICulture|DefaultThreadCurrentCulture|DefaultThreadCurrentUICulture)\s*(\?\?)?=(?!=)",
            RegexOptions.Compiled);

        var sourceRoot = Path.Combine(LocalizationSourceScan.RepositoryRoot, "src");
        var offenders = LocalizationSourceScan.EnumerateProductionSource()
            .Where(path => pinPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Production code must not pin thread culture (it follows the OS). Offending files: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Resolve_InvariantCulture_TerminatesAtNeutral()
    {
        var resolved = ContentCulture.Resolve(CultureInfo.InvariantCulture, ContentCulture.SupportedUiCultures);

        Assert.Equal("en", resolved.Name);
        Assert.Equal("ltr", ContentCulture.DirectionOf(resolved));
    }

    [Fact]
    public void Resolve_SupportedRtlCulture_ReturnsThatCultureRtl()
    {
        // Once a culture is in the supported set, its own direction is honored.
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en", "ar" };

        var resolved = ContentCulture.Resolve(CultureInfo.GetCultureInfo("ar-SA"), supported);

        Assert.Equal("ar", resolved.Name);
        Assert.Equal("rtl", ContentCulture.DirectionOf(resolved));
    }

    [Theory]
    [InlineData("en-US", "en", "ltr")]
    [InlineData("en-GB", "en", "ltr")]
    [InlineData("en", "en", "ltr")]
    [InlineData("ar-SA", "en", "ltr")] // unsupported RTL OS -> neutral en/ltr today (no regression)
    [InlineData("zh-Hans-CN", "en", "ltr")]
    public void Resolve_UnsupportedCulture_FallsBackToNeutralLtr(string current, string expectedName, string expectedDir)
    {
        var resolved = ContentCulture.Resolve(
            CultureInfo.GetCultureInfo(current),
            ContentCulture.SupportedUiCultures);

        Assert.Equal(expectedName, resolved.Name);
        Assert.Equal(expectedDir, ContentCulture.DirectionOf(resolved));
    }

    [Theory]
    [InlineData("Information", "[[Severity_Level_Information]]")]
    [InlineData("6", "6")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SeverityLevelLocalizer_RawLabel_LocalizesKnownLevelsAndPreservesUnknownRawValues(string? rawLevel, string expected)
    {
        Assert.Equal(expected, SeverityLevelLocalizer.Label(new MarkerLocalizer(), rawLevel));
    }

    [Fact]
    public void SharedResourceNeutralResolver_ResolvesKnownKeyWithArguments()
    {
        var resolver = new SharedResourceNeutralResolver();

        string resolved = resolver.Resolve(DatabaseToolsLogKeys.RunnerProtocolMismatch, ["3", "4"]);

        Assert.Contains("3", resolved, StringComparison.Ordinal);
        Assert.Contains("4", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("{1}", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusBarMemorySizes_UseCurrentCultureDecimalSeparator()
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            string value = StatusBarTextComposer.MemoryValue(BuildLocalizer(), 1536, MemoryUsageLevel.Normal);

            Assert.Contains("1,5", value, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    [Fact]
    public void StatusBarNumberFormatting_GroupsLoadingAndNewEvents()
    {
        IStringLocalizer<SharedResource> localizer = BuildLocalizer();
        var loading = StatusBarTextComposer.Loading(
            localizer,
            ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty.Add(
                StatusActivityId.Create(),
                new LoadingProgress(1500, 0)));

        Assert.NotNull(loading);
        Assert.Contains(1500.ToString("N0", CultureInfo.CurrentCulture), loading.Value.Text, StringComparison.Ordinal);

        using var context = new StatusBarRenderContext(newEventCount: 1000);

        var cut = context.Render<UI.StatusBar.StatusBar>();

        Assert.Contains(1000.ToString("N0", CultureInfo.CurrentCulture), cut.Find("button.status-bar-newevents").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportedUiCultures_MatchesEmbeddedSatellites_ExcludingCanary()
    {
        // Neutral comes from the assembly's [NeutralResourcesLanguage] so changing/removing it fails this guard, not silently passes.
        var neutralAttribute = typeof(SharedResource).Assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>();
        Assert.NotNull(neutralAttribute);
        var neutral = CultureInfo.GetCultureInfo(neutralAttribute.CultureName).Name;

        // Derive the satellite filename from the catalog assembly so this guard follows the resource assembly if it is ever relocated again.
        var satelliteFileName = typeof(SharedResource).Assembly.GetName().Name + ".resources.dll";
        var satelliteCultures = Directory.GetDirectories(AppContext.BaseDirectory)
            .Where(directory => File.Exists(Path.Combine(directory, satelliteFileName)))
            .Select(directory => CultureInfo.GetCultureInfo(Path.GetFileName(directory)).Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Positive control: the qps-ploc canary satellite must be discovered, so a zero-satellite result (e.g. a renamed resource assembly) fails loudly here instead of passing this guard vacuously.
        var canary = CultureInfo.GetCultureInfo("qps-ploc").Name;
        Assert.True(
            satelliteCultures.Contains(canary),
            $"No '{canary}' satellite ('{satelliteFileName}') was discovered under '{AppContext.BaseDirectory}'. " +
            "The satellite drift-guard cannot function without it; verify the resource assembly name and packaging.");

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { neutral };
        expected.UnionWith(satelliteCultures.Where(name => !string.Equals(name, canary, StringComparison.OrdinalIgnoreCase)));

        Assert.True(
            ContentCulture.SupportedUiCultures.SetEquals(expected),
            $"SupportedUiCultures [{string.Join(", ", ContentCulture.SupportedUiCultures)}] must equal " +
            $"neutral-plus-non-canary-satellites [{string.Join(", ", expected)}]. Ship a translation's culture here " +
            "ONLY after the RTL prerequisite bundle lands.");
    }

    [Fact]
    public void UpdatesAndTitleNeutralValues_HaveExpectedPlaceholderArity()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("Modal_Yes", 0),
            ("Modal_No", 0),
            ("Update_Alert_CheckUnavailable_Title", 0),
            ("Update_Alert_CheckUnavailable_Message", 0),
            ("Update_Alert_NoUpdates_Title", 0),
            ("Update_Alert_NoUpdates_Message", 0),
            ("Update_Alert_Failure_Title", 0),
            ("Update_Alert_RetrieveFailed_Message", 1),
            ("Update_Alert_InstallFailed_Message", 1),
            ("Update_Alert_Unavailable_Title", 0),
            ("Update_Alert_Unavailable_Message", 0),
            ("Update_Alert_Available_Title", 0),
            ("Update_Alert_Available_Message", 0),
            ("Update_Alert_ReleaseNotesFailed_Title", 0),
            ("Update_Alert_ReleaseNotesFailed_Message", 0),
            ("AppTitle_Qualifier_Development", 0),
            ("AppTitle_Qualifier_Preview", 0),
            ("AppTitle_Qualifier_Admin", 0),
            ("AppTitle_Progress_Installing", 1),
            ("AppTitle_Progress_Relaunch", 0),
            ("ReleaseNotes_TitleWithVersion", 1),
            ("ReleaseNotes_AriaLabel", 0)
        ];

        foreach ((string key, int arity) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(arity, PlaceholderArity(neutral));
        }
    }

    [Fact]
    public void UpdatesAndTitleWhitespaceSensitiveValues_PreserveLoadBearingSeparatorsThroughResourceManager()
    {
        var localizer = BuildLocalizer();

        foreach (string key in new[]
        {
            "AppTitle_Qualifier_Development",
            "AppTitle_Qualifier_Preview",
            "AppTitle_Qualifier_Admin"
        })
        {
            string value = localizer[key].Value;

            Assert.StartsWith(" ", value, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }

        foreach (string key in new[]
        {
            "Update_Alert_RetrieveFailed_Message",
            "Update_Alert_InstallFailed_Message"
        })
        {
            Assert.EndsWith("\r\n{0}", localizer[key].Value, StringComparison.Ordinal);
        }
    }

    // Distinct 4-digit sentinel per argument index (2002, 3003, 4004, ...): all plural (never the "one" boundary) and
    // mutually non-substring in both raw and grouped ("2,002") form, so one argument's value cannot mask another's
    // presence or grouping when the render is searched.
    private static int ArgumentSentinel(int index) => (1000 * (index + 2)) + (index + 2);

    private static void AssertNoOffenders(IReadOnlyList<string> offenders, string message) =>
        Assert.True(offenders.Count == 0, $"{message}: {string.Join("; ", offenders)}");

    // Verifies that a migrated single-key plural pattern behaves as intended, without freezing its English copy. The
    // checks are engineered so a false pass is hard to manufacture:
    //   1. Structure  - the selector variable and authored CLDR category set must match expectations, so a resx edit
    //                   that renames the selector or adds/drops a branch fails loudly.
    //   2. Selection  - only the selector varies across the 1/2 boundary (other arguments pinned), so a surviving
    //                   normalized difference (or its absence) is attributable to branch selection alone.
    //   3. Per-branch landing + grouping - EVERY authored branch is rendered (selector := 1 for "one", a large plural
    //                   sentinel for "other", the literal for "=N") with each argument at a DISTINCT sentinel, and each
    //                   argument must appear in its expected numeric form (and NOT the opposite form). This catches an
    //                   argument dropped or reformatted in ONLY one branch. Grouping of the selector itself is checked
    //                   only where its value is large: at count 1 a grouped and a raw selector render identically, so
    //                   that case has no observable behavior to guard.
    //   4. Order / swap - in the all-distinct render the arguments must appear in the caller's declared TEXTUAL order,
    //                   so swapping two same-format placeholders in the pattern (which binds values by name) is caught
    //                   by an oracle independent of the pattern itself.
    // Callers therefore declare `arguments` in the order the placeholders appear in the neutral English text.
    private static void AssertPluralPatternBehavior(
        IReadOnlyDictionary<string, string> neutralValues,
        string key,
        string selector,
        IReadOnlyList<PluralArgument> arguments,
        IReadOnlyList<string> expectedCategories,
        RenderShape renderShape,
        string? expectedPrefix = null)
    {
        Assert.True(neutralValues.TryGetValue(key, out string? pattern), $"Missing neutral RESX value for {key}.");

        (string parsedSelector, IReadOnlyList<string> parsedCategories) = ParsePluralShape(pattern!);
        Assert.Equal(selector, parsedSelector);
        Assert.Equal(
            expectedCategories.OrderBy(category => category, StringComparer.Ordinal),
            parsedCategories.OrderBy(category => category, StringComparer.Ordinal));

        int selectorSentinel = ArgumentSentinel(IndexOfArgument(arguments, selector));

        string singular = RenderPlural(pattern!, SentinelValues(arguments, (selector, 1)));
        string plural = RenderPlural(pattern!, SentinelValues(arguments, (selector, 2)));

        if (renderShape == RenderShape.SingularPluralDiffer)
        {
            Assert.NotEqual(NormalizeFormattedNumbers(singular), NormalizeFormattedNumbers(plural));
        }
        else
        {
            Assert.Equal(NormalizeFormattedNumbers(singular), NormalizeFormattedNumbers(plural));
        }

        foreach (string category in expectedCategories)
        {
            int selectorValue = SelectorValueForCategory(category, selectorSentinel);
            string branch = RenderPlural(pattern!, SentinelValues(arguments, (selector, selectorValue)));
            int previousIndex = -1;

            foreach (PluralArgument argument in arguments)
            {
                int value = argument.Name == selector ? selectorValue : ArgumentSentinel(IndexOfArgument(arguments, argument.Name));
                string groupedForm = value.ToString("N0", CultureInfo.InvariantCulture);
                string rawForm = value.ToString(CultureInfo.InvariantCulture);
                string expectedForm = argument.Grouped ? groupedForm : rawForm;

                Assert.Contains(expectedForm, branch, StringComparison.Ordinal);

                if (value >= 1000)
                {
                    Assert.DoesNotContain(argument.Grouped ? rawForm : groupedForm, branch, StringComparison.Ordinal);
                }

                // Declared textual order must hold IN THIS branch, so a swap confined to a single branch is caught. The
                // value is matched on numeric-token boundaries so a small selector value (e.g. "1") is never found
                // inside another argument's digits.
                int index = IndexOfNumericToken(branch, expectedForm);
                Assert.True(index > previousIndex, $"Argument '{argument.Name}' ({expectedForm}) is out of declared textual order in the '{category}' branch: '{branch}'.");
                previousIndex = index;
            }

            if (expectedPrefix is not null)
            {
                Assert.StartsWith(expectedPrefix, branch, StringComparison.Ordinal);
            }
        }
    }

    // Resolves the localizer from a bare container: the production extension plus the ILoggerFactory it needs (as the host supplies in production).
    private static IStringLocalizer<SharedResource> BuildLocalizer() =>
        new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddEventLogLocalization()
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();

    private static int CountLocalizableTextArguments(string argumentsExpression)
    {
        argumentsExpression = argumentsExpression.Trim();

        if (argumentsExpression == "[]" || string.IsNullOrEmpty(argumentsExpression)) { return 0; }

        if (argumentsExpression.StartsWith('[') && argumentsExpression.EndsWith(']'))
        {
            return SplitArguments(argumentsExpression[1..^1]).Count(argument => !string.IsNullOrWhiteSpace(argument));
        }

        return -1;
    }

    private static IReadOnlyDictionary<string, string> DatabaseToolsKeyConstants() =>
        typeof(DatabaseToolsLogKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateRepositoryTextFiles(params string[] roots)
    {
        string[] extensions =
        [
            ".cs",
            ".csproj",
            ".json",
            ".md",
            ".props",
            ".razor",
            ".resx",
            ".targets",
            ".xaml",
            ".xml"
        ];

        foreach (string root in roots)
        {
            string fullRoot = Path.Combine(LocalizationSourceScan.RepositoryRoot, root);

            foreach (string path in Directory.EnumerateFiles(fullRoot, "*.*", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return path;
            }
        }
    }

    private static IReadOnlyList<string> ExtractMethodCalls(string source, string methodName)
    {
        var calls = new List<string>();
        string token = methodName + "(";
        var index = 0;

        while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            var start = index + token.Length;
            var depth = 1;
            var inString = false;
            var escaped = false;

            for (var position = start; position < source.Length; position++)
            {
                char current = source[position];

                if (inString)
                {
                    if (escaped) { escaped = false; }
                    else if (current == '\\') { escaped = true; }
                    else if (current == '"') { inString = false; }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '(') { depth++; }
                else if (current == ')' && --depth == 0)
                {
                    calls.Add(source[start..position]);
                    index = position + 1;
                    break;
                }
            }

            index++;
        }

        return calls;
    }

    private static IReadOnlyList<RenderedPlaceholder> ExtractRenderedPlaceholders(string pattern)
    {
        var placeholders = new List<RenderedPlaceholder>();

        ParseRenderedPlaceholders(pattern, 0, pattern.Length, placeholders);

        return placeholders;
    }

    // Reports any retired count-driven One/Many key selection in production source. The testable core
    // `ScanForLegacyCountDrivenSelections` works on whole statement spans, so a precomputed local is caught even when
    // the eventual localization call is a separate statement; the per-span decision lives in
    // IsLegacyCountDrivenPluralSelection, which the positive-control test exercises directly.
    private static IReadOnlyList<string> FindLegacyCountDrivenKeySelections() =>
        LocalizationSourceScan.EnumerateProductionSource()
            .SelectMany(path => ScanForLegacyCountDrivenSelections(
                File.ReadAllText(path),
                Path.GetRelativePath(LocalizationSourceScan.RepositoryRoot, path)))
            .OrderBy(offender => offender, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> FindLegacyPluralResourceKeys() =>
        ResxKeys()
            .Where(IsLegacyPluralKeyName)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

    private static int FindMatchingBrace(string text, int openBrace, int end)
    {
        int depth = 0;

        for (int position = openBrace; position < end; position++)
        {
            if (text[position] == '{') { depth++; }
            else if (text[position] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return position;
                }
            }
        }

        return end;
    }

    private static IReadOnlyList<string> FindTextOccurrences(IEnumerable<string> paths, string token) =>
        paths.SelectMany(path =>
            File.ReadLines(path)
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(entry => entry.Line.Contains(token, StringComparison.Ordinal))
                .Select(entry => $"{Path.GetRelativePath(LocalizationSourceScan.RepositoryRoot, path)}:{entry.Number}:{entry.Line.Trim()}"))
            .OrderBy(offender => offender, StringComparer.Ordinal)
            .ToList();

    private static PluralArgument Grouped(string name) => new(name, Grouped: true);

    private static int IndexOfArgument(IReadOnlyList<PluralArgument> arguments, string name)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index].Name == name) { return index; }
        }

        Assert.Fail($"Declared arguments do not include '{name}'.");
        return -1;
    }

    // Finds a rendered numeric value as a standalone token (not embedded in another number), so a selector rendered as
    // "1" is not matched inside "1000" or another argument's grouped digits.
    private static int IndexOfNumericToken(string text, string numericForm)
    {
        Match match = Regex.Match(text, $@"(?<![\d,]){Regex.Escape(numericForm)}(?![\d,])");

        return match.Success ? match.Index : -1;
    }

    private static bool IsExactGroupedIntegerFormat(string? format) =>
        string.Equals(format, "N0", StringComparison.OrdinalIgnoreCase);

    // A code fragment that selects a retired One/Many key from a count comparison. Requires BOTH signals so an ordinary
    // "count == 1" branch or an unrelated method named "SelectMany" is not mistaken for the retired mechanism.
    private static bool IsLegacyCountDrivenPluralSelection(string code) =>
        s_countOneComparison.IsMatch(code) && ReferencesLegacyPluralKeyToken(code);

    // A resx/key-constant name that still encodes the retired two-form selector: a "_One"/"_Many" segment terminal or
    // before a later suffix ("Foo_One_Ambiguous"), a CamelCase "One"/"Many" word (lowercase/digit before;
    // uppercase/underscore/end after, so real words like "ManyLogs" and "None" are spared), or the retired four-way
    // "_ItemN_DupN"/"_Accept_NN" spellings. The exact contract - including the spared names - is pinned by
    // LegacyPluralDetectors_Flag... below.
    private static bool IsLegacyPluralKeyName(string name) =>
        s_legacyTerminalPluralSegment.IsMatch(name) ||
        s_legacyCamelCasePluralWord.IsMatch(name) ||
        s_legacyFourWayPluralKey.IsMatch(name);

    private static int LineNumber(string source, int index) =>
        source.Take(index).Count(character => character == '\n') + 1;

    private static string NormalizeFormattedNumbers(string value) =>
        Regex.Replace(value, @"\d+(?:,\d{3})*(?:\.\d+)?", "#");

    private static int ParseIcuBlock(
        string text,
        int openBrace,
        int end,
        List<RenderedPlaceholder> placeholders)
    {
        int position = openBrace + 1;
        SkipWhiteSpace(text, ref position, end);
        string name = ParsePlaceholderName(text, ref position, end);
        SkipWhiteSpace(text, ref position, end);

        if (position >= end)
        {
            return end;
        }

        if (text[position] == '}')
        {
            placeholders.Add(new RenderedPlaceholder(name, null));
            return position + 1;
        }

        if (text[position] == ':')
        {
            position++;
            int formatStart = position;

            while (position < end && text[position] != '}') { position++; }

            placeholders.Add(new RenderedPlaceholder(name, text[formatStart..position]));
            return position < end ? position + 1 : end;
        }

        if (text[position] != ',')
        {
            return SkipBalancedBlock(text, openBrace, end);
        }

        position++;
        SkipWhiteSpace(text, ref position, end);
        string kind = ParsePlaceholderName(text, ref position, end);
        SkipWhiteSpace(text, ref position, end);

        if (!string.Equals(kind, "plural", StringComparison.Ordinal) || position >= end || text[position] != ',')
        {
            return SkipBalancedBlock(text, openBrace, end);
        }

        position++;
        return ParsePluralBranches(text, position, end, placeholders);
    }

    private static string ParsePlaceholderName(string text, ref int position, int end)
    {
        int start = position;

        while (position < end && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position++;
        }

        return text[start..position];
    }

    private static int ParsePluralBranches(
        string text,
        int start,
        int end,
        List<RenderedPlaceholder> placeholders)
    {
        int position = start;

        while (position < end)
        {
            SkipWhiteSpace(text, ref position, end);

            if (position >= end)
            {
                return end;
            }

            if (text[position] == '}')
            {
                return position + 1;
            }

            while (position < end && text[position] != '{') { position++; }

            if (position >= end)
            {
                return end;
            }

            int branchStart = position + 1;
            int branchEnd = FindMatchingBrace(text, position, end);
            ParseRenderedPlaceholders(text, branchStart, branchEnd, placeholders);
            position = branchEnd + 1;
        }

        return end;
    }

    // Extracts the selector variable and the top-level CLDR category tokens ("one", "other", "=1", ...) from an ICU
    // plural pattern, skipping balanced branch bodies so nested placeholders like {total:N0} are never mistaken for a
    // category.
    private static (string Selector, IReadOnlyList<string> Categories) ParsePluralShape(string pattern)
    {
        string trimmed = pattern.Trim();
        Assert.StartsWith("{", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("}", trimmed, StringComparison.Ordinal);

        string body = trimmed[1..^1];
        int firstComma = body.IndexOf(',');
        Assert.True(firstComma > 0, $"Plural pattern '{pattern}' has no selector.");
        int secondComma = body.IndexOf(',', firstComma + 1);
        Assert.True(secondComma > firstComma, $"Plural pattern '{pattern}' has no plural keyword.");
        Assert.Equal("plural", body[(firstComma + 1)..secondComma].Trim());

        var categories = new List<string>();
        int position = secondComma + 1;

        while (position < body.Length)
        {
            while (position < body.Length && char.IsWhiteSpace(body[position])) { position++; }
            if (position >= body.Length) { break; }

            int tokenStart = position;
            while (position < body.Length && body[position] != '{') { position++; }
            Assert.True(position < body.Length, $"Plural category in '{pattern}' has no branch body.");
            categories.Add(body[tokenStart..position].Trim());

            int depth = 0;
            do
            {
                if (body[position] == '{') { depth++; }
                else if (body[position] == '}') { depth--; }
                position++;
            }
            while (position < body.Length && depth > 0);

            Assert.Equal(0, depth);
        }

        return (body[..firstComma].Trim(), categories);
    }

    private static void ParseRenderedPlaceholders(
        string text,
        int start,
        int end,
        List<RenderedPlaceholder> placeholders)
    {
        int position = start;

        while (position < end)
        {
            if (text[position] == '{')
            {
                position = ParseIcuBlock(text, position, end, placeholders);
                continue;
            }

            position++;
        }
    }

    private static int PlaceholderArity(string value)
    {
        var indexes = Regex.Matches(value, @"\{(\d+)(?::[^}]*)?\}")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        return indexes.Count == 0 ? 0 : indexes.Max() + 1;
    }

    private static IReadOnlyList<string> PluralQuantityPlaceholderOffenders(
        IEnumerable<KeyValuePair<string, string>> patterns,
        ISet<string>? observedQuantityArguments = null)
    {
        HashSet<string> quantityArguments = new(s_pluralQuantityArguments, StringComparer.Ordinal);
        HashSet<string> allowedBareArguments = new(s_pluralRenderedPlaceholderAllowlist, StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach ((string key, string pattern) in patterns)
        {
            foreach (RenderedPlaceholder placeholder in ExtractRenderedPlaceholders(pattern))
            {
                if (allowedBareArguments.Contains(placeholder.Name))
                {
                    if (placeholder.Format is not null)
                    {
                        offenders.Add($"{key}: allowlisted {{{placeholder.Name}:{placeholder.Format}}} must not carry a format");
                    }

                    continue;
                }

                if (key == "DatabaseTools_Op_CreateSkippedProviders" &&
                    placeholder.Name == "0" &&
                    placeholder.Format is null)
                {
                    continue;
                }

                if (quantityArguments.Contains(placeholder.Name))
                {
                    observedQuantityArguments?.Add(placeholder.Name);

                    if (!IsExactGroupedIntegerFormat(placeholder.Format))
                    {
                        offenders.Add($"{key}: quantity {{{placeholder.Name}{(placeholder.Format is null ? string.Empty : ":" + placeholder.Format)}}} must use :N0");
                    }

                    continue;
                }

                offenders.Add($"{key}: unclassified {{{placeholder.Name}{(placeholder.Format is null ? string.Empty : ":" + placeholder.Format)}}}");
            }
        }

        return offenders;
    }

    private static PluralArgument Raw(string name) => new(name, Grouped: false);

    // True when the code references a retired plural KEY as a quoted string literal. Each quoted identifier is checked
    // with the same IsLegacyPluralKeyName predicate as the resource-key guard, so the legitimate
    // "StatusBar_Loading_ManyLogs" key (and ordinary words) are spared even though they contain "Many".
    private static bool ReferencesLegacyPluralKeyToken(string code)
    {
        foreach (Match match in s_quotedIdentifierLiteral.Matches(code))
        {
            if (IsLegacyPluralKeyName(match.Groups[1].Value)) { return true; }
        }

        return false;
    }

    private static string RenderPlural(string pattern, IReadOnlyDictionary<string, int> argumentValues)
    {
        Dictionary<string, object?> values = argumentValues.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value,
            StringComparer.Ordinal);

        return new IcuMessageFormatter().Format(
            pattern,
            values,
            CultureInfo.InvariantCulture,
            CultureInfo.GetCultureInfo("en-US"));
    }

    private static IReadOnlyList<string> ResxKeys() =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!.Elements("data")
            .Select(data => (string?)data.Attribute("name"))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();

    private static IReadOnlyDictionary<string, string> ResxValues() =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!.Elements("data")
            .Where(data => data.Attribute("name") is not null)
            .ToDictionary(
                data => (string)data.Attribute("name")!,
                data => data.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    // Splits source into ';'-delimited statement spans and reports each that both compares a count to 1 and references
    // a retired One/Many key. Statement granularity (rather than only Localizer[...]/LocalizableText(...) spans) is what
    // lets a precomputed `var key = count == 1 ? "X_One" : "X_Many";` be detected.
    private static IReadOnlyList<string> ScanForLegacyCountDrivenSelections(string source, string label)
    {
        var offenders = new List<string>();
        int spanStart = 0;

        for (int index = 0; index <= source.Length; index++)
        {
            if (index < source.Length && source[index] != ';') { continue; }

            string span = source[spanStart..index];

            if (IsLegacyCountDrivenPluralSelection(span))
            {
                offenders.Add($"{label}:{LineNumber(source, spanStart)}:{SingleLine(span)}");
            }

            spanStart = index + 1;
        }

        return offenders;
    }

    // The selector value that lands rendering in a given authored branch: 1 selects "one", a large plural sentinel
    // selects "other" (never "one"/"=N"), and "=N" is selected by its own literal.
    private static int SelectorValueForCategory(string category, int otherSentinel)
    {
        if (category == "one") { return 1; }
        if (category == "other") { return otherSentinel; }
        if (category.StartsWith("=", StringComparison.Ordinal)) { return int.Parse(category[1..], CultureInfo.InvariantCulture); }

        Assert.Fail($"Unhandled plural category '{category}'.");
        return 0;
    }

    // Assigns each argument its distinct sentinel; an optional override pins one argument (the selector) to a specific
    // count so the caller can hold the field fixed while probing the 1/2 selection boundary.
    private static IReadOnlyDictionary<string, int> SentinelValues(
        IReadOnlyList<PluralArgument> arguments,
        (string Selector, int Value)? selectorOverride)
    {
        Dictionary<string, int> values = new(StringComparer.Ordinal);

        for (int index = 0; index < arguments.Count; index++)
        {
            values[arguments[index].Name] = ArgumentSentinel(index);
        }

        if (selectorOverride is { } selector)
        {
            values[selector.Selector] = selector.Value;
        }

        return values;
    }

    private static string SingleLine(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim();

    private static int SkipBalancedBlock(string text, int openBrace, int end)
    {
        int closeBrace = FindMatchingBrace(text, openBrace, end);

        return closeBrace < end ? closeBrace + 1 : end;
    }

    private static void SkipWhiteSpace(string text, ref int position, int end)
    {
        while (position < end && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    private static IReadOnlyList<string> SplitArguments(string callArguments)
    {
        var arguments = new List<string>();
        var start = 0;
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = 0; index < callArguments.Length; index++)
        {
            char current = callArguments[index];

            if (inString)
            {
                if (escaped) { escaped = false; }
                else if (current == '\\') { escaped = true; }
                else if (current == '"') { inString = false; }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current is '(' or '[' or '{') { depth++; }
            else if (current is ')' or ']' or '}') { depth--; }
            else if (current == ',' && depth == 0)
            {
                arguments.Add(callArguments[start..index].Trim());
                start = index + 1;
            }
        }

        arguments.Add(callArguments[start..].Trim());
        return arguments;
    }

    private static bool TryResolveDatabaseToolsKey(
        string expression,
        IReadOnlyDictionary<string, string> constants,
        out string? key)
    {
        const string prefix = "DatabaseToolsLogKeys.";
        expression = expression.Trim();

        if (!expression.StartsWith(prefix, StringComparison.Ordinal) ||
            !constants.TryGetValue(expression[prefix.Length..], out key))
        {
            key = null;
            return false;
        }

        return true;
    }

    private sealed class StatusBarRenderContext : BunitContext
    {
        public StatusBarRenderContext(int newEventCount)
        {
            JSInterop.Mode = JSRuntimeMode.Loose;

            var eventLogCommands = Substitute.For<IEventLogCommands>();
            var filterApplied = Substitute.For<IFilterAppliedSource>();
            var lensSource = Substitute.For<IFilterLensSource>();
            var modalCoordinator = Substitute.For<IModalCoordinator>();
            var statsCommands = Substitute.For<IStatsCommands>();
            var statsVisibility = Substitute.For<IStatsVisibilitySource>();
            var statusBarSource = Substitute.For<IStatusBarSource>();
            var viewSource = Substitute.For<IOrderedViewSource>();

            var eventLogId = EventLogId.Create();
            var view = Substitute.For<IEventColumnView>();
            view.Count.Returns(0);

            viewSource.Current.Returns(_ => new OrderedViewPresentation(view, eventLogId, default, PresentationState.Current, Revision: 1));
            filterApplied.IsFilteringEnabled.Returns(false);
            lensSource.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
            statusBarSource.Current.Returns(new StatusBarPresentation
            {
                Tabs = ImmutableList.Create(new LogView(eventLogId) { LogName = "Application", LogPathType = LogPathType.Channel }),
                ActiveTabId = eventLogId,
                RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(eventLogId, default),
                NewEventBufferCount = newEventCount
            });

            Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            Services.AddEventLogLocalization();
            Services.AddSingleton(eventLogCommands);
            Services.AddSingleton(filterApplied);
            Services.AddSingleton(lensSource);
            Services.AddSingleton(modalCoordinator);
            Services.AddSingleton(statsCommands);
            Services.AddSingleton(statsVisibility);
            Services.AddSingleton(statusBarSource);
            Services.AddSingleton(viewSource);
            Services.AddSingleton(provider => new DisplayIndicatorGate(provider.GetRequiredService<IOrderedViewSource>()));
        }
    }

    // A declared plural argument plus whether the pattern must group it (":N0") or emit it raw.
    private readonly record struct PluralArgument(string Name, bool Grouped);

    private readonly record struct RenderedPlaceholder(string Name, string? Format);
}

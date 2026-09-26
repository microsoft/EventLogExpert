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
        Assert.Equal(
            "Database '{0}' is at schema v{1}; this version is no longer supported. Upgrade through an older EventLogExpert release that supports v3 first, or delete the file.",
            neutralValues[DatabaseToolsLogKeys.UpgradeUnsupportedV1OrV2Schema]);
    }

    [Fact]
    public void DebugLogPlaceholderValues_HaveExpectedPlaceholderArity()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity)[] expected =
        [
            ("DebugLog_Footer_Counter_One", 2),
            ("DebugLog_Footer_Counter_Many", 2),
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
        // literals, so scanning the whole call span never misreads an argument as a key. LocalizedCount.OneOrMany picks
        // its key by count at runtime, so its singular/plural pair never lands inside a Localizer[...] indexer; a second
        // pattern anchored on the trailing quoted pair recovers both keys (a parenthesis in the count arg cannot truncate it).
        var localizerCallPattern = new Regex(
            @"[Ll]ocalizer\[([^\]]*)\]|\.GetString\(([^)]*)\)",
            RegexOptions.Compiled);
        var keyLiteralPattern = new Regex(@"""([A-Za-z0-9_]+)""", RegexOptions.Compiled);
        var oneOrManyPattern = new Regex(
            @"OneOrMany(?:Raw)?\([^;{}]*?""([A-Za-z0-9_]+)""\s*,\s*""([A-Za-z0-9_]+)""(?:\s*,\s*""([A-Za-z0-9_]+)""\s*,\s*""([A-Za-z0-9_]+)"")?",
            RegexOptions.Compiled);

        var sources = LocalizationSourceScan.EnumerateProductionSource()
            .Select(File.ReadAllText)
            .ToList();

        var referenced = sources
            .SelectMany(source => localizerCallPattern.Matches(source))
            .SelectMany(call => keyLiteralPattern.Matches(call.Groups[1].Value + call.Groups[2].Value))
            .Select(match => match.Groups[1].Value)
            .Concat(sources
                .SelectMany(source => oneOrManyPattern.Matches(source))
                .SelectMany(call => Enumerable.Range(1, 4)
                    .Select(index => call.Groups[index].Value)
                    .Where(value => value.Length > 0)))
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

    [Fact]
    public void FilePickerNeutralValues_HaveExpectedArityAndByteExactEnglish()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity, string Value)[] expected =
        [
            ("FilePicker_Error_FirstExtensionBlank", 0, "The first extension cannot be null or whitespace."),
            ("FilePicker_Error_NoExtensions", 0, "At least one extension must be supplied."),
            ("FilePicker_Error_NoFileTypeChoice", 0, "At least one file-type choice must be supplied."),
            ("FilePicker_Filter_AllFiles", 0, "All files"),
            ("FilePicker_Filter_SupportedTypes", 1, "Supported types ({0})"),
            ("FilePicker_Title_OpenEventLogs", 0, "Open Event Logs"),
            ("FilePicker_Title_SaveAs", 0, "Save As"),
            ("FilePicker_Title_SelectFolder", 0, "Select Folder")
        ];

        foreach (var (key, arity, value) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(value, neutral);
            Assert.Equal(arity, PlaceholderArity(neutral));
        }
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
            ("Banner_Attention_Many", 1),
            ("Banner_Attention_One", 1),
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
            ("Banner_Db_Import_Partial_Many", 1),
            ("Banner_Db_Import_Partial_Message", 2),
            ("Banner_Db_Import_Partial_One", 0),
            ("Banner_Db_Import_Partial_Title", 0),
            ("Banner_Db_Import_Success_Many", 1),
            ("Banner_Db_Import_Success_One", 0),
            ("Banner_Db_Import_Success_Title", 0),
            ("Banner_Db_Import_UpgradeFailurePart", 2),
            ("Banner_Db_OperationFailed_Message", 2),
            ("Banner_Db_OperationNoun_Import", 0),
            ("Banner_Db_OperationNoun_Toggle", 1),
            ("Banner_Db_OperationNoun_UpgradeBatch_Many", 1),
            ("Banner_Db_OperationNoun_UpgradeBatch_One", 1),
            ("Banner_Db_OperationNoun_UpgradeSingle", 1),
            ("Banner_Db_RemoveFailed_Message", 2),
            ("Banner_Db_RemoveFailed_Title", 0),
            ("Banner_Db_UpdateFailed_Title", 0),
            ("Banner_Db_UpgradeFailed_Message", 2),
            ("Banner_Db_UpgradeFailed_Title", 0),
            ("Banner_EmptyLog_Many", 2),
            ("Banner_EmptyLog_One", 1),
            ("Banner_EmptyLog_Title", 0),
            ("Banner_Error_DismissAria", 0),
            ("Banner_Export_Blocked_Faulted", 0),
            ("Banner_Export_Blocked_InProgress", 0),
            ("Banner_Export_Blocked_NoColumns", 0),
            ("Banner_Export_Blocked_NoEvents", 0),
            ("Banner_Export_Blocked_Updating", 0),
            ("Banner_Export_Canceled_Message", 0),
            ("Banner_Export_Canceled_Title", 0),
            ("Banner_Export_Complete_Many", 2),
            ("Banner_Export_Complete_One", 2),
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
            ("Banner_Filter_NotLoaded_Message_Many", 1),
            ("Banner_Filter_NotLoaded_Message_One", 0),
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
            ("Banner_Recovery_Needed_Many", 1),
            ("Banner_Recovery_Needed_One", 0),
            ("Banner_Recovery_Needed_Title", 0),
            ("Banner_Recovery_Resolve", 0),
            ("Banner_Upgrade_InProgress", 4),
            ("Banner_Upgrade_Preparing_Many", 1),
            ("Banner_Upgrade_Preparing_One", 1),
            ("Banner_Upgrade_QueuedBatches_Many", 1),
            ("Banner_Upgrade_QueuedBatches_One", 1)
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
            ("FilterImport_Added_Many", 1),
            ("FilterImport_Added_One", 1),
            ("FilterImport_Ambiguous_Many", 1),
            ("FilterImport_Ambiguous_One", 1),
            ("FilterImport_Announcement_TagRemoved_Many", 2),
            ("FilterImport_Announcement_TagRemoved_One", 2),
            ("FilterImport_Announcement_TagRenamed_Many", 3),
            ("FilterImport_Announcement_TagRenamed_One", 3),
            ("FilterImport_BlockedHeader", 0),
            ("FilterImport_EmptyValueMessage_Many", 2),
            ("FilterImport_EmptyValueMessage_One", 2),
            ("FilterImport_EmptyValueTitle", 0),
            ("FilterImport_Error_EmptyFile", 0),
            ("FilterImport_Error_EntryIdExpectedJsonString", 1),
            ("FilterImport_Error_EntryIdExpectedNonEmptyString", 0),
            ("FilterImport_Error_EntryIdInvalidGuid", 1),
            ("FilterImport_Error_InvalidBasicFilters_Many", 1),
            ("FilterImport_Error_InvalidBasicFilters_One", 1),
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
            ("FilterImport_KeepEmpty_Many", 1),
            ("FilterImport_KeepEmpty_One", 1),
            ("FilterImport_MoreNames", 1),
            ("FilterImport_NothingToImport_ItemMany_DupMany", 2),
            ("FilterImport_NothingToImport_ItemMany_DupOne", 2),
            ("FilterImport_NothingToImport_ItemOne_DupMany", 2),
            ("FilterImport_NothingToImport_ItemOne_DupOne", 2),
            ("FilterImport_Overwrite_Many", 1),
            ("FilterImport_Overwrite_One", 1),
            ("FilterImport_OverwriteNamesHeader", 0),
            ("FilterImport_PreviewHeader", 0),
            ("FilterImport_RemovedEmptyNotice_Many", 1),
            ("FilterImport_RemovedEmptyNotice_One", 1),
            ("FilterImport_Renames_Many", 1),
            ("FilterImport_Renames_One", 1),
            ("FilterImport_Skipped_Many", 1),
            ("FilterImport_Skipped_One", 1),
            ("FilterImport_Summary_TagMany", 4),
            ("FilterImport_Summary_TagMany_Ambiguous", 5),
            ("FilterImport_Summary_TagOne", 4),
            ("FilterImport_Summary_TagOne_Ambiguous", 5),
            ("FilterImport_TagUpdates_Many", 1),
            ("FilterImport_TagUpdates_One", 1),
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
    public void NeutralHistogramCountsRemainUngrouped()
    {
        IStringLocalizer<SharedResource> localizer = BuildLocalizer();

        Assert.Equal(
            "Other (1200 sources)",
            HistogramGroupLabelFormatter.Format(localizer, new HistogramGroupLabel.CategoricalOther(HistogramDimension.Source, 1200)));
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
    public void NeutralHistogramSpaceBearingValues_KeepExactWhitespace()
    {
        var neutralValues = ResxValues();

        Assert.Equal(", ", neutralValues["Histogram_Breakdown_Separator"]);
        Assert.Equal(" - {0} error/critical", neutralValues["Stats_Headline_ErrorCritical_One"]);
        Assert.Equal(" - {0} error/critical", neutralValues["Stats_Headline_ErrorCritical_Many"]);
        Assert.Equal(" - top {0} source = {1}%", neutralValues["Stats_Headline_TopSources_One"]);
        Assert.Equal(" - top {0} sources = {1}%", neutralValues["Stats_Headline_TopSources_Many"]);
    }

    [Fact]
    public void NeutralHistogramSummaryTemplates_KeepGeneralShortDateFormat()
    {
        var neutralValues = ResxValues();

        Assert.Equal("Timeline: {0} {1} from {2:g} to {3:g}.", neutralValues["Histogram_RegionAria"]);
        Assert.Equal("Timeline: {0} {1} from {2:g} to {3:g}, {4}.", neutralValues["Histogram_RegionAria_Breakdown"]);
        Assert.Equal("Showing {2:g} to {3:g}: {0} {1}.", neutralValues["Histogram_WindowAnnouncement"]);
        Assert.Equal("Showing {2:g} to {3:g}: {0} {1}, {4}.", neutralValues["Histogram_WindowAnnouncement_Breakdown"]);
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
            ("Db_Entry_MixedOs_Count", 1),
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
            ("Db_Manage_Upgrade_Cancelled_Many", 2),
            ("Db_Manage_Upgrade_Cancelled_One", 2),
            ("Db_Manage_Upgrade_MultipleFailure", 5),
            ("Db_Manage_Upgrade_SingleFailure", 2),
            ("Db_Manage_Upgrade_Success_Many", 1),
            ("Db_Manage_Upgrade_Success_One", 1),
            ("Db_Picker_ImportPrompt", 0),
            ("DatabaseRecoveryModal_Delete", 0),
            ("DatabaseRecoveryModal_DeleteAll", 0),
            ("DatabaseRecoveryModal_Description_Many", 0),
            ("DatabaseRecoveryModal_Description_One", 0),
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
        Assert.Equal(6, neutralValues.Keys.Count(key => key.StartsWith("Db_Manage_Upgrade_", StringComparison.Ordinal)));
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
    public void NeutralStatusBarValues_KeepByteIdenticalEnglish()
    {
        var neutralValues = ResxValues();
        (string Key, string Value)[] expected =
        [
            ("StatusBar_Source_None", "No log open"),
            ("StatusBar_Source_AllLogs", "All logs ({0})"),
            ("StatusBar_Source_Combined", "Combined"),
            ("StatusBar_Source_CombinedCount_One", "Combined ({0} logs)"),
            ("StatusBar_Source_CombinedCount_Many", "Combined ({0} logs)"),
            ("StatusBar_Counts_Total_One", "{0} events"),
            ("StatusBar_Counts_Total_Many", "{0} events"),
            ("StatusBar_Counts_TotalSelected", "{0} events · {1} selected"),
            ("StatusBar_Counts_ShownOfTotal", "{0} of {1} shown"),
            ("StatusBar_Counts_ShownOfTotalSelected", "{0} of {1} shown · {2} selected"),
            ("StatusBar_Coverage_Chip", "{0} unresolved"),
            ("StatusBar_Coverage_AriaLabel", "Resolution and coverage: {0} unresolved. Open for details."),
            ("StatusBar_Coverage_Tooltip", "{0} unresolved of {1} events loaded in this tab/group. Filters are not applied - open Coverage for the current view's breakdown."),
            ("StatusBar_Loading_Pending", "Loading..."),
            ("StatusBar_Loading_PendingPercent", "Loading... ({0}%)"),
            ("StatusBar_Loading_Count", "Loading: {0}"),
            ("StatusBar_Loading_CountPercent", "Loading: {0} ({1}%)"),
            ("StatusBar_Loading_ManyLogs", "Loading {0} logs..."),
            ("StatusBar_Loading_Failed", "Failed: {0}"),
            ("StatusBar_Memory_Value_Normal", "Memory: {0}"),
            ("StatusBar_Memory_Value_Elevated", "Memory: {0} · Elevated"),
            ("StatusBar_Memory_Value_High", "Memory: {0} · High"),
            ("StatusBar_Memory_Announce_Normal", "Memory usage normal"),
            ("StatusBar_Memory_Announce_Elevated", "Memory usage elevated"),
            ("StatusBar_Memory_Announce_High", "Memory usage high"),
            ("StatusBar_Memory_Tooltip_Normal", "Managed heap (app data): {0} - drops as logs close. Process working set: {1} - the OS may release this later."),
            ("StatusBar_Memory_Tooltip_Elevated", "Managed heap (app data): {0} - drops as logs close. Process working set: {1} - the OS may release this later. Level: elevated."),
            ("StatusBar_Memory_Tooltip_High", "Managed heap (app data): {0} - drops as logs close. Process working set: {1} - the OS may release this later. Level: high."),
            ("StatusBar_Activity_Fault", "These events could not be prepared"),
            ("StatusBar_Activity_BufferFull", "Buffer full"),
            ("StatusBar_Activity_Loading", "Loading"),
            ("StatusBar_Activity_LoadingEvents", "Loading events"),
            ("StatusBar_Activity_Reordering", "Reordering events"),
            ("StatusBar_Activity_ContinuouslyUpdating", "Continuously updating"),
            ("StatusBar_Resolver_FailedToOpen", "Error: Failed to open {0}"),
            ("StatusBar_Resolver_NoResolver", "Error: No event resolver available"),
            ("StatusBar_Resolver_FailedToLoad", "Error: Failed to load {0}"),
            ("StatusBar_Filter_Chip", "Filtered"),
            ("StatusBar_Filter_Active", "Filter active"),
            ("StatusBar_Filter_Lens_One", "1 lens"),
            ("StatusBar_Filter_Lens_Many", "{0} lenses"),
            ("StatusBar_Filter_ActiveLens_One", "Filter + 1 lens"),
            ("StatusBar_Filter_ActiveLens_Many", "Filter + {0} lenses"),
            ("StatusBar_Stats_Show", "Show statistics for these events"),
            ("StatusBar_Stats_Hide", "Hide statistics for these events"),
            ("StatusBar_NewEvents_Label", "New Events: {0}"),
            ("StatusBar_NewEvents_None", "No new events to load"),
            ("StatusBar_NewEvents_Load", "Load new events into the view")
        ];

        foreach ((string key, string value) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? actual), $"Missing neutral RESX value for {key}.");
            Assert.Equal(value, actual);
        }

        Assert.Equal(
            expected.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal),
            neutralValues.Keys.Where(key => key.StartsWith("StatusBar_", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal));
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

    [Fact]
    public void SharedResourceNeutralResolver_ResolvesKnownKeyWithArguments()
    {
        var resolver = new SharedResourceNeutralResolver();

        string resolved = resolver.Resolve(DatabaseToolsLogKeys.RunnerProtocolMismatch, ["3", "4"]);

        Assert.Equal(
            "Helper IPC protocol version mismatch: helper sent 3, runner expected 4. The helper EXE may be from a different app version - reinstall the MSIX so the main app and helper ship together.",
            resolved);
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
    public void StatusBarNumberFormatting_MixesGroupedLoadingAndRawNewEvents()
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

        Assert.Contains("1000", cut.Find("button.status-bar-newevents").TextContent, StringComparison.Ordinal);
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
    public void UpdatesAndTitleNeutralValues_HaveExpectedArityAndByteExactEnglish()
    {
        var neutralValues = ResxValues();
        (string Key, int Arity, string Value)[] expected =
        [
            ("Modal_Yes", 0, "Yes"),
            ("Modal_No", 0, "No"),
            ("Update_Alert_CheckUnavailable_Title", 0, "Update Check Unavailable"),
            ("Update_Alert_CheckUnavailable_Message", 0, "Update checks are disabled for development builds."),
            ("Update_Alert_NoUpdates_Title", 0, "No Updates Available"),
            ("Update_Alert_NoUpdates_Message", 0, "You are currently running the latest version."),
            ("Update_Alert_Failure_Title", 0, "Update Failure"),
            ("Update_Alert_RetrieveFailed_Message", 1, "Failed to retrieve latest releases:\r\n{0}"),
            ("Update_Alert_InstallFailed_Message", 1, "Update failed to install:\r\n{0}"),
            ("Update_Alert_Unavailable_Title", 0, "Update Unavailable"),
            ("Update_Alert_Unavailable_Message", 0, "No compatible update package was found."),
            ("Update_Alert_Available_Title", 0, "Update Available"),
            ("Update_Alert_Available_Message", 0,
                "A new version has been detected, would you like to install and reload the application?"),
            ("Update_Alert_ReleaseNotesFailed_Title", 0, "Release Notes Failure"),
            ("Update_Alert_ReleaseNotesFailed_Message", 0, "Failed to get release notes for the current version"),
            ("AppTitle_Qualifier_Development", 0, " (Development)"),
            ("AppTitle_Qualifier_Preview", 0, " (Preview)"),
            ("AppTitle_Qualifier_Admin", 0, " (Admin)"),
            ("AppTitle_Progress_Installing", 1, "Installing: {0}%"),
            ("AppTitle_Progress_Relaunch", 0, "Relaunch to Apply Update"),
            ("ReleaseNotes_TitleWithVersion", 1, "Release notes for v{0}"),
            ("ReleaseNotes_AriaLabel", 0, "Release Notes")
        ];

        foreach (var (key, arity, value) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(value, neutral);
            Assert.Equal(arity, PlaceholderArity(neutral));
        }
    }

    [Fact]
    public void UpdatesAndTitleWhitespaceSensitiveValues_ResolveByteExactThroughResourceManager()
    {
        // The XML-parse guard above cannot catch a dropped xml:space="preserve" (XDocument preserves leaf
        // whitespace unconditionally); resolving through the compiled ResourceManager does, protecting the
        // window-title byte-identity that depends on the leading-space qualifiers.
        var localizer = BuildLocalizer();
        (string Key, string Value)[] expected =
        [
            ("AppTitle_Qualifier_Development", " (Development)"),
            ("AppTitle_Qualifier_Preview", " (Preview)"),
            ("AppTitle_Qualifier_Admin", " (Admin)"),
            ("Update_Alert_RetrieveFailed_Message", "Failed to retrieve latest releases:\r\n{0}"),
            ("Update_Alert_InstallFailed_Message", "Update failed to install:\r\n{0}")
        ];

        foreach (var (key, value) in expected)
        {
            Assert.Equal(value, localizer[key].Value);
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

    private static int PlaceholderArity(string value)
    {
        var indexes = Regex.Matches(value, @"\{(\d+)(?::[^}]*)?\}")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        return indexes.Count == 0 ? 0 : indexes.Max() + 1;
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
}

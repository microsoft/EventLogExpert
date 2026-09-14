// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.Common;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterLibrary;

internal static class FilterImportTextComposer
{
    private const string BulletPrefix = "  \u2022 ";
    private const int MaxPreviewedImportNames = 10;

    internal static string EmptyValueMessage(IStringLocalizer<SharedResource> localizer, IReadOnlyList<string> entryNames) =>
        LocalizedCount.OneOrManyRaw(
            localizer,
            entryNames.Count,
            "FilterImport_EmptyValueMessage_One",
            "FilterImport_EmptyValueMessage_Many",
            entryNames.Count,
            string.Join(", ", entryNames));

    internal static string EmptyValueTitle(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterImport_EmptyValueTitle"];

    internal static string ImportAsIsAction(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterImport_Action_ImportAsIs"];

    internal static string ImportConfirmation(
        IStringLocalizer<SharedResource> localizer,
        ImportPreflight preflight,
        bool keptEmptyValuesAsIs)
    {
        List<string> notices = [];

        if (keptEmptyValuesAsIs && preflight.NormalizableEmptyValueEntryNames.Count > 0)
        {
            var count = preflight.NormalizableEmptyValueEntryNames.Count;

            notices.Add(LocalizedCount.OneOrManyRaw(
                localizer,
                count,
                "FilterImport_KeepEmpty_One",
                "FilterImport_KeepEmpty_Many",
                count));
        }

        if (preflight.NormalizeRemovedFilterNames.Count > 0)
        {
            var count = preflight.NormalizeRemovedFilterNames.Count;

            notices.Add(LocalizedCount.OneOrManyRaw(
                localizer,
                count,
                "FilterImport_RemovedEmptyNotice_One",
                "FilterImport_RemovedEmptyNotice_Many",
                count));
        }

        var preview = Preview(localizer, preflight);

        return notices.Count > 0 ? string.Join("\n", notices) + "\n\n" + preview : preview;
    }

    internal static string NormalizeAction(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterImport_Action_Normalize"];

    internal static string NothingToImport(IStringLocalizer<SharedResource> localizer, ImportPreflight preflight) =>
        LocalizedCount.OneOrManyRaw(
            localizer,
            preflight.NormalizeRemovedFilterNames.Count,
            preflight.SkippedDuplicates.Count,
            "FilterImport_NothingToImport_ItemOne_DupOne",
            "FilterImport_NothingToImport_ItemOne_DupMany",
            "FilterImport_NothingToImport_ItemMany_DupOne",
            "FilterImport_NothingToImport_ItemMany_DupMany",
            preflight.NormalizeRemovedFilterNames.Count,
            preflight.SkippedDuplicates.Count);

    internal static string Preview(IStringLocalizer<SharedResource> localizer, ImportPreflight preflight)
    {
        if (preflight.ImportBlocked)
        {
            return localizer["FilterImport_BlockedHeader"] + "\n" + FormatNameList(localizer, preflight.InvalidLegacyNames);
        }

        List<string> lines =
        [
            BulletPrefix + LocalizedCount.OneOrManyRaw(
                localizer,
                preflight.ToAdd.Count,
                "FilterImport_Added_One",
                "FilterImport_Added_Many",
                preflight.ToAdd.Count)
        ];

        if (preflight.ToReplace.Count > 0)
        {
            var conflictList = "\n" + localizer["FilterImport_OverwriteNamesHeader"] + "\n" +
                FormatNameList(localizer, [.. preflight.ToReplace.Select(pair => pair.Incoming.Name)]);

            lines.Add(BulletPrefix + LocalizedCount.OneOrManyRaw(
                localizer,
                preflight.ToReplace.Count,
                "FilterImport_Overwrite_One",
                "FilterImport_Overwrite_Many",
                preflight.ToReplace.Count) + conflictList);
        }

        if (preflight.ToUpdate.Count > 0)
        {
            var standaloneTagUpdates = FilterLibraryModal.CountStandaloneTagUpdates(preflight);

            if (standaloneTagUpdates > 0)
            {
                lines.Add(BulletPrefix + LocalizedCount.OneOrManyRaw(
                    localizer,
                    standaloneTagUpdates,
                    "FilterImport_TagUpdates_One",
                    "FilterImport_TagUpdates_Many",
                    standaloneTagUpdates));
            }

            var renameCount = preflight.ToUpdate.Count(pair => pair.Existing.Name.Contains('\\'));

            if (renameCount > 0)
            {
                lines.Add(BulletPrefix + LocalizedCount.OneOrManyRaw(
                    localizer,
                    renameCount,
                    "FilterImport_Renames_One",
                    "FilterImport_Renames_Many",
                    renameCount));
            }
        }

        if (preflight.AmbiguousMatches.Count > 0)
        {
            lines.Add(BulletPrefix + LocalizedCount.OneOrManyRaw(
                localizer,
                preflight.AmbiguousMatches.Count,
                "FilterImport_Ambiguous_One",
                "FilterImport_Ambiguous_Many",
                preflight.AmbiguousMatches.Count));
        }

        lines.Add(BulletPrefix + LocalizedCount.OneOrManyRaw(
            localizer,
            preflight.SkippedDuplicates.Count,
            "FilterImport_Skipped_One",
            "FilterImport_Skipped_Many",
            preflight.SkippedDuplicates.Count));

        return localizer["FilterImport_PreviewHeader"] + "\n" + string.Join('\n', lines);
    }

    internal static string Summary(IStringLocalizer<SharedResource> localizer, ImportSummary summary) =>
        summary.Ambiguous > 0
            ? LocalizedCount.OneOrManyRaw(
                localizer,
                summary.UpdatedTags,
                "FilterImport_Summary_TagOne_Ambiguous",
                "FilterImport_Summary_TagMany_Ambiguous",
                summary.Added,
                summary.Replaced,
                summary.UpdatedTags,
                summary.Skipped,
                summary.Ambiguous)
            : LocalizedCount.OneOrManyRaw(
                localizer,
                summary.UpdatedTags,
                "FilterImport_Summary_TagOne",
                "FilterImport_Summary_TagMany",
                summary.Added,
                summary.Replaced,
                summary.UpdatedTags,
                summary.Skipped);

    internal static string TagRemoved(IStringLocalizer<SharedResource> localizer, string tag, int count) =>
        LocalizedCount.OneOrManyRaw(
            localizer,
            count,
            "FilterImport_Announcement_TagRemoved_One",
            "FilterImport_Announcement_TagRemoved_Many",
            tag,
            count);

    internal static string TagRenamed(IStringLocalizer<SharedResource> localizer, string oldTag, string newTag, int count) =>
        LocalizedCount.OneOrManyRaw(
            localizer,
            count,
            "FilterImport_Announcement_TagRenamed_One",
            "FilterImport_Announcement_TagRenamed_Many",
            oldTag,
            newTag,
            count);

    private static string FormatNameList(IStringLocalizer<SharedResource> localizer, IReadOnlyList<string> names)
    {
        var visible = names.Take(MaxPreviewedImportNames).ToList();
        var text = BulletPrefix + string.Join("\n" + BulletPrefix, visible);

        return names.Count > MaxPreviewedImportNames ?
            text + "\n" + BulletPrefix + localizer["FilterImport_MoreNames", names.Count - MaxPreviewedImportNames] :
            text;
    }
}

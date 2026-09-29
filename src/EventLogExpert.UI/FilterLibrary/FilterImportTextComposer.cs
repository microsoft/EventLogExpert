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
        PluralText.Format(
            localizer,
            "FilterImport_EmptyValueMessage",
            ("count", entryNames.Count),
            ("entryNames", string.Join(", ", entryNames)));

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

            notices.Add(PluralText.Format(localizer, "FilterImport_KeepEmpty", ("count", count)));
        }

        if (preflight.NormalizeRemovedFilterNames.Count > 0)
        {
            var count = preflight.NormalizeRemovedFilterNames.Count;

            notices.Add(PluralText.Format(localizer, "FilterImport_RemovedEmptyNotice", ("count", count)));
        }

        var preview = Preview(localizer, preflight);

        return notices.Count > 0 ? string.Join("\n", notices) + "\n\n" + preview : preview;
    }

    internal static string NormalizeAction(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterImport_Action_Normalize"];

    internal static string NothingToImport(IStringLocalizer<SharedResource> localizer, ImportPreflight preflight) =>
        PluralText.Format(
            localizer,
            "FilterImport_NothingToImport",
            ("itemCount", preflight.NormalizeRemovedFilterNames.Count),
            ("duplicateCount", preflight.SkippedDuplicates.Count));

    internal static string Preview(IStringLocalizer<SharedResource> localizer, ImportPreflight preflight)
    {
        if (preflight.ImportBlocked)
        {
            return localizer["FilterImport_BlockedHeader"] + "\n" + FormatNameList(localizer, preflight.InvalidLegacyNames);
        }

        List<string> lines =
        [
            BulletPrefix + PluralText.Format(localizer, "FilterImport_Added", ("count", preflight.ToAdd.Count))
        ];

        if (preflight.ToReplace.Count > 0)
        {
            var conflictList = "\n" + localizer["FilterImport_OverwriteNamesHeader"] + "\n" +
                FormatNameList(localizer, [.. preflight.ToReplace.Select(pair => pair.Incoming.Name)]);

            lines.Add(BulletPrefix + PluralText.Format(localizer, "FilterImport_Overwrite", ("count", preflight.ToReplace.Count)) + conflictList);
        }

        if (preflight.ToUpdate.Count > 0)
        {
            var standaloneTagUpdates = FilterLibraryModal.CountStandaloneTagUpdates(preflight);

            if (standaloneTagUpdates > 0)
            {
                lines.Add(BulletPrefix + PluralText.Format(localizer, "FilterImport_TagUpdates", ("count", standaloneTagUpdates)));
            }

            var renameCount = preflight.ToUpdate.Count(pair => pair.Existing.Name.Contains('\\'));

            if (renameCount > 0)
            {
                lines.Add(BulletPrefix + PluralText.Format(localizer, "FilterImport_Renames", ("count", renameCount)));
            }
        }

        if (preflight.AmbiguousMatches.Count > 0)
        {
            lines.Add(BulletPrefix + PluralText.Format(localizer, "FilterImport_Ambiguous", ("count", preflight.AmbiguousMatches.Count)));
        }

        lines.Add(BulletPrefix + PluralText.Format(localizer, "FilterImport_Skipped", ("count", preflight.SkippedDuplicates.Count)));

        return localizer["FilterImport_PreviewHeader"] + "\n" + string.Join('\n', lines);
    }

    internal static string Summary(IStringLocalizer<SharedResource> localizer, ImportSummary summary) =>
        PluralText.Format(
            localizer,
            summary.Ambiguous > 0 ? "FilterImport_Summary_Tag_Ambiguous" : "FilterImport_Summary_Tag",
            ("added", summary.Added),
            ("replaced", summary.Replaced),
            ("updatedTags", summary.UpdatedTags),
            ("skipped", summary.Skipped),
            ("ambiguous", summary.Ambiguous));

    internal static string TagRemoved(IStringLocalizer<SharedResource> localizer, string tag, int count) =>
        PluralText.Format(localizer, "FilterImport_Announcement_TagRemoved", ("tag", tag), ("count", count));

    internal static string TagRenamed(IStringLocalizer<SharedResource> localizer, string oldTag, string newTag, int count) =>
        PluralText.Format(
            localizer,
            "FilterImport_Announcement_TagRenamed",
            ("oldTag", oldTag),
            ("newTag", newTag),
            ("count", count));

    private static string FormatNameList(IStringLocalizer<SharedResource> localizer, IReadOnlyList<string> names)
    {
        var visible = names.Take(MaxPreviewedImportNames).ToList();
        var text = BulletPrefix + string.Join("\n" + BulletPrefix, visible);

        return names.Count > MaxPreviewedImportNames ?
            text + "\n" + BulletPrefix + localizer["FilterImport_MoreNames", names.Count - MaxPreviewedImportNames] :
            text;
    }
}

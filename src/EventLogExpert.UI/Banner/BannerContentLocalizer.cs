// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Banner;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

internal static partial class BannerContentLocalizer
{
    internal static BannerContentText Resolve(IStringLocalizer<SharedResource> localizer, BannerMessage content) =>
        content switch
        {
            ExportBlocked exportBlocked => ResolveExportBlocked(localizer, exportBlocked),
            ExportCanceled => new BannerContentText(
                localizer["Banner_Export_Canceled_Title"],
                localizer["Banner_Export_Canceled_Message"]),
            ExportFailed exportFailed => new BannerContentText(
                localizer["Banner_Export_Failed_Title"],
                exportFailed.Detail),
            ExportComplete exportComplete => new BannerContentText(
                localizer["Banner_Export_Complete_Title"],
                localizer[
                    exportComplete.Count == 1 ? "Banner_Export_Complete_One" : "Banner_Export_Complete_Many",
                    exportComplete.Count,
                    exportComplete.Path]),
            DatabaseRemoveFailed databaseRemoveFailed => new BannerContentText(
                localizer["Banner_Db_RemoveFailed_Title"],
                localizer["Banner_Db_RemoveFailed_Message", databaseRemoveFailed.FileName, databaseRemoveFailed.Detail]),
            DatabaseUpgradeFailed databaseUpgradeFailed => new BannerContentText(
                localizer["Banner_Db_UpgradeFailed_Title"],
                localizer["Banner_Db_UpgradeFailed_Message", databaseUpgradeFailed.FileName, databaseUpgradeFailed.Reason]),
            DatabaseOperationFailed databaseOperationFailed => ResolveDatabaseOperationFailed(
                localizer,
                databaseOperationFailed),
            DatabaseImportSummary databaseImportSummary => ResolveDatabaseImportSummary(localizer, databaseImportSummary),
            FilterLibraryNotFullyLoaded filterLibraryNotFullyLoaded => new BannerContentText(
                localizer["Banner_Filter_NotLoaded_Title"],
                localizer["Banner_Filter_NotLoaded_Message", filterLibraryNotFullyLoaded.Count]),
            FilterSetSaveFailed filterSetSaveFailed => new BannerContentText(
                localizer["Banner_Filter_SaveFailed_Title"],
                localizer["Banner_Filter_SaveFailed_Message", filterSetSaveFailed.Name]),
            EmptyLogs emptyLogs => new BannerContentText(
                localizer["Banner_EmptyLog_Title"],
                emptyLogs.DisplayNames.Count == 1
                    ? localizer["Banner_EmptyLog_One", emptyLogs.DisplayNames[0]]
                    : localizer["Banner_EmptyLog_Many", emptyLogs.DisplayNames.Count, string.Join(", ", emptyLogs.DisplayNames)]),
            Preformatted preformatted => new BannerContentText(
                preformatted.Title,
                preformatted.Message,
                preformatted.ActionLabel),
            _ => throw new ArgumentOutOfRangeException(nameof(content), content.GetType(), "Unknown banner content type.")
        };
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterPane;

internal static class FilterPaneAnnouncements
{
    internal static string LoadFailedRetryViaModal(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterPane_Announcement_LoadFailedRetryViaModal"];

    internal static string LoadingTryAgain(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterPane_Announcement_LoadingTryAgain"];

    internal static string RecentNoneAvailable(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterPane_Announcement_RecentNoneAvailable"];

    internal static string SelectedFilterSetMissing(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterPane_Announcement_SelectedFilterSetMissing"];

    internal static string SelectedScenarioMissing(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterPane_Announcement_SelectedScenarioMissing"];
}

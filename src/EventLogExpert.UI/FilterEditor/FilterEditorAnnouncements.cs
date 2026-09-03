// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterEditor;

internal static class FilterEditorAnnouncements
{
    public static string EditCancelled(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterEditor_Announcement_EditCancelled"];

    public static string EditingFilter(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterEditor_Announcement_EditingFilter"];

    public static string FilterDiscarded(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterEditor_Announcement_FilterDiscarded"];

    public static string FilterEnabledState(IStringLocalizer<SharedResource> localizer, bool newIsEnabled) =>
        newIsEnabled ?
            localizer["FilterEditor_Announcement_FilterEnabled"] :
            localizer["FilterEditor_Announcement_FilterDisabled"];

    public static string FilterRemoved(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterEditor_Announcement_FilterRemoved"];

    public static string FilterSaved(IStringLocalizer<SharedResource> localizer) =>
        localizer["FilterEditor_Announcement_FilterSaved"];

    public static string FilterSetTo(IStringLocalizer<SharedResource> localizer, bool isExcluded) =>
        isExcluded ?
            localizer["FilterEditor_Announcement_SetToExclude"] :
            localizer["FilterEditor_Announcement_SetToInclude"];

    public static string SwitchedToMode(IStringLocalizer<SharedResource> localizer, FilterMode mode) => mode switch
    {
        FilterMode.Advanced => localizer["FilterEditor_Announcement_SwitchedToAdvanced"],
        FilterMode.Basic => localizer["FilterEditor_Announcement_SwitchedToBasic"],
        FilterMode.Cached => localizer["FilterEditor_Announcement_SwitchedToRecent"],
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}

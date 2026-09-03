// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterEditor;

internal static class FilterEditorModeSwitchLocalizer
{
    public static string ConfirmationMessage(
        IStringLocalizer<SharedResource> localizer,
        FilterMode current,
        FilterMode target) => (current, target) switch
        {
            (FilterMode.Advanced, FilterMode.Cached) => localizer["FilterEditor_ModeSwitch_Message_ToRecent"],
            (FilterMode.Basic, FilterMode.Cached) => localizer["FilterEditor_ModeSwitch_Message_ToRecent"],
            (FilterMode.Cached, FilterMode.Cached) => localizer["FilterEditor_ModeSwitch_Message_ToRecent"],
            (FilterMode.Advanced, FilterMode.Basic) => localizer["FilterEditor_ModeSwitch_Message_ToBasic"],
            (FilterMode.Cached, FilterMode.Basic) => localizer["FilterEditor_ModeSwitch_Message_ToBasic"],
            (FilterMode.Basic, FilterMode.Advanced) => localizer["FilterEditor_ModeSwitch_Message_ToAdvanced"],
            (FilterMode.Cached, FilterMode.Advanced) => localizer["FilterEditor_ModeSwitch_Message_Generic"],
            (FilterMode.Advanced, FilterMode.Advanced) => localizer["FilterEditor_ModeSwitch_Message_Generic"],
            (FilterMode.Basic, FilterMode.Basic) => localizer["FilterEditor_ModeSwitch_Message_Generic"],
            _ => throw new ArgumentOutOfRangeException(nameof(current), current, null)
        };
}

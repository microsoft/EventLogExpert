// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterEditor;

internal static class FilterEditorHighlightColorLocalizer
{
    public static string Label(IStringLocalizer<SharedResource> localizer, HighlightColor color) => color switch
    {
        HighlightColor.None => localizer["FilterEditor_HighlightColorOption_None"],
        HighlightColor.LightRed => localizer["FilterEditor_HighlightColorOption_LightRed"],
        HighlightColor.Red => localizer["FilterEditor_HighlightColorOption_Red"],
        HighlightColor.DarkRed => localizer["FilterEditor_HighlightColorOption_DarkRed"],
        HighlightColor.LightOrange => localizer["FilterEditor_HighlightColorOption_LightOrange"],
        HighlightColor.Orange => localizer["FilterEditor_HighlightColorOption_Orange"],
        HighlightColor.DarkOrange => localizer["FilterEditor_HighlightColorOption_DarkOrange"],
        HighlightColor.LightYellow => localizer["FilterEditor_HighlightColorOption_LightYellow"],
        HighlightColor.Yellow => localizer["FilterEditor_HighlightColorOption_Yellow"],
        HighlightColor.DarkYellow => localizer["FilterEditor_HighlightColorOption_DarkYellow"],
        HighlightColor.LightGreen => localizer["FilterEditor_HighlightColorOption_LightGreen"],
        HighlightColor.Green => localizer["FilterEditor_HighlightColorOption_Green"],
        HighlightColor.DarkGreen => localizer["FilterEditor_HighlightColorOption_DarkGreen"],
        HighlightColor.LightTeal => localizer["FilterEditor_HighlightColorOption_LightTeal"],
        HighlightColor.Teal => localizer["FilterEditor_HighlightColorOption_Teal"],
        HighlightColor.DarkTeal => localizer["FilterEditor_HighlightColorOption_DarkTeal"],
        HighlightColor.LightBlue => localizer["FilterEditor_HighlightColorOption_LightBlue"],
        HighlightColor.Blue => localizer["FilterEditor_HighlightColorOption_Blue"],
        HighlightColor.DarkBlue => localizer["FilterEditor_HighlightColorOption_DarkBlue"],
        HighlightColor.LightPurple => localizer["FilterEditor_HighlightColorOption_LightPurple"],
        HighlightColor.Purple => localizer["FilterEditor_HighlightColorOption_Purple"],
        HighlightColor.DarkPurple => localizer["FilterEditor_HighlightColorOption_DarkPurple"],
        HighlightColor.LightMagenta => localizer["FilterEditor_HighlightColorOption_LightMagenta"],
        HighlightColor.Magenta => localizer["FilterEditor_HighlightColorOption_Magenta"],
        HighlightColor.DarkMagenta => localizer["FilterEditor_HighlightColorOption_DarkMagenta"],
        HighlightColor.LightPink => localizer["FilterEditor_HighlightColorOption_LightPink"],
        HighlightColor.Pink => localizer["FilterEditor_HighlightColorOption_Pink"],
        HighlightColor.DarkPink => localizer["FilterEditor_HighlightColorOption_DarkPink"],
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
    };
}

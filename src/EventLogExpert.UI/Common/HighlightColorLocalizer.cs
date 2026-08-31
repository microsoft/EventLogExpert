// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class HighlightColorLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, HighlightColor color) => color switch
    {
        HighlightColor.None => localizer["Histogram_HighlightColor_None"],
        HighlightColor.LightRed => localizer["Histogram_HighlightColor_LightRed"],
        HighlightColor.Red => localizer["Histogram_HighlightColor_Red"],
        HighlightColor.DarkRed => localizer["Histogram_HighlightColor_DarkRed"],
        HighlightColor.LightOrange => localizer["Histogram_HighlightColor_LightOrange"],
        HighlightColor.Orange => localizer["Histogram_HighlightColor_Orange"],
        HighlightColor.DarkOrange => localizer["Histogram_HighlightColor_DarkOrange"],
        HighlightColor.LightYellow => localizer["Histogram_HighlightColor_LightYellow"],
        HighlightColor.Yellow => localizer["Histogram_HighlightColor_Yellow"],
        HighlightColor.DarkYellow => localizer["Histogram_HighlightColor_DarkYellow"],
        HighlightColor.LightGreen => localizer["Histogram_HighlightColor_LightGreen"],
        HighlightColor.Green => localizer["Histogram_HighlightColor_Green"],
        HighlightColor.DarkGreen => localizer["Histogram_HighlightColor_DarkGreen"],
        HighlightColor.LightTeal => localizer["Histogram_HighlightColor_LightTeal"],
        HighlightColor.Teal => localizer["Histogram_HighlightColor_Teal"],
        HighlightColor.DarkTeal => localizer["Histogram_HighlightColor_DarkTeal"],
        HighlightColor.LightBlue => localizer["Histogram_HighlightColor_LightBlue"],
        HighlightColor.Blue => localizer["Histogram_HighlightColor_Blue"],
        HighlightColor.DarkBlue => localizer["Histogram_HighlightColor_DarkBlue"],
        HighlightColor.LightPurple => localizer["Histogram_HighlightColor_LightPurple"],
        HighlightColor.Purple => localizer["Histogram_HighlightColor_Purple"],
        HighlightColor.DarkPurple => localizer["Histogram_HighlightColor_DarkPurple"],
        HighlightColor.LightMagenta => localizer["Histogram_HighlightColor_LightMagenta"],
        HighlightColor.Magenta => localizer["Histogram_HighlightColor_Magenta"],
        HighlightColor.DarkMagenta => localizer["Histogram_HighlightColor_DarkMagenta"],
        HighlightColor.LightPink => localizer["Histogram_HighlightColor_LightPink"],
        HighlightColor.Pink => localizer["Histogram_HighlightColor_Pink"],
        HighlightColor.DarkPink => localizer["Histogram_HighlightColor_DarkPink"],
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
    };
}

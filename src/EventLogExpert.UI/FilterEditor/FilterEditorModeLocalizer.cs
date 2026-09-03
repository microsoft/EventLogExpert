// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterEditor;

internal static class FilterEditorModeLocalizer
{
    public static string Display(IStringLocalizer<SharedResource> localizer, FilterMode mode) => mode switch
    {
        FilterMode.Advanced => localizer["FilterEditor_Mode_Advanced"],
        FilterMode.Basic => localizer["FilterEditor_Mode_Basic"],
        FilterMode.Cached => localizer["FilterEditor_Mode_Recent"],
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}

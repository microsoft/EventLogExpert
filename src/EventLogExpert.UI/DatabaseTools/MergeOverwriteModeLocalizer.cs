// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.DatabaseTools;

internal static class MergeOverwriteModeLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, MergeOverwriteMode mode) => mode switch
    {
        MergeOverwriteMode.Keep => localizer["MergeOverwriteMode_Keep"],
        MergeOverwriteMode.Overwrite => localizer["MergeOverwriteMode_Overwrite"],
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}

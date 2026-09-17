// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.DatabaseTools;

internal static class AutoImportModeLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, AutoImportMode mode) => mode switch
    {
        AutoImportMode.ImportAndEnable => localizer["AutoImportMode_ImportAndEnable"],
        AutoImportMode.Import => localizer["AutoImportMode_Import"],
        AutoImportMode.Off => localizer["AutoImportMode_Off"],
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}

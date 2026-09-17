// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.DatabaseTools;

internal static class AutoImportStateLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, AutoImportState state) => state switch
    {
        AutoImportState.Importing => localizer["AutoImportState_Importing"],
        AutoImportState.Imported => localizer["AutoImportState_Imported"],
        AutoImportState.Failed => localizer["AutoImportState_Failed"],
        AutoImportState.NotImported => localizer["AutoImportState_NotImported"],
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };
}

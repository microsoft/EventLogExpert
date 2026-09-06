// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Banner;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Banner;

internal static partial class BannerContentLocalizer
{
    private static BannerContentText ResolveExportBlocked(
        IStringLocalizer<SharedResource> localizer,
        ExportBlocked content) =>
        new(
            localizer["Banner_Export_Title"],
            content.Reason switch
            {
                ExportBlockReason.Faulted => localizer["Banner_Export_Blocked_Faulted"],
                ExportBlockReason.Updating => localizer["Banner_Export_Blocked_Updating"],
                ExportBlockReason.NoEvents => localizer["Banner_Export_Blocked_NoEvents"],
                ExportBlockReason.NoColumns => localizer["Banner_Export_Blocked_NoColumns"],
                ExportBlockReason.AlreadyInProgress => localizer["Banner_Export_Blocked_InProgress"],
                _ => throw new ArgumentOutOfRangeException(nameof(content), content.Reason, "Unknown export block reason.")
            });
}

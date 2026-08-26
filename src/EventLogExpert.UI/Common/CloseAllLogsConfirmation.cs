// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class CloseAllLogsConfirmation
{
    public static Task<bool> ConfirmAsync(IAlertDialogService dialog, IStringLocalizer<SharedResource> localizer) =>
        dialog.ShowAlert(
            localizer["CloseAllLogs_Title"],
            localizer["CloseAllLogs_Body"],
            localizer["CloseAllLogs_Confirm"],
            localizer["Modal_Cancel"]);
}

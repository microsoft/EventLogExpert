// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;

namespace EventLogExpert.UI.Common;

internal static class CloseAllLogsConfirmation
{
    public static Task<bool> ConfirmAsync(IAlertDialogService dialog) =>
        dialog.ShowAlert("Close all logs", "Close all open logs? This cannot be undone.", "Close all", "Cancel");
}

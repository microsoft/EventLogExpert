// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;

namespace EventLogExpert.UI.Common;

internal static class EnableChannelConfirmation
{
    public static Task<bool> ConfirmAsync(IAlertDialogService dialog, string channelName, bool isAnalyticOrDebug)
    {
        var message =
            $"Enable the \"{channelName}\" log?\n\n" +
            "This changes Windows event logging for the whole computer and stays in effect until it is disabled again. " +
            "Only events generated after enabling are collected - events that occurred while the log was disabled cannot be recovered.";

        if (isAnalyticOrDebug)
        {
            message +=
                "\n\nThis is an analytic or debug log: enabling it can clear the records it already holds, and Windows may " +
                "keep collecting to it while refusing to open it for live viewing until it is disabled again.";
        }

        return dialog.ShowAlert("Enable log", message, "Enable", "Cancel");
    }
}

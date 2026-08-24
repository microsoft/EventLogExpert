// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class EnableChannelConfirmation
{
    public static Task<bool> ConfirmAsync(
        IAlertDialogService dialog,
        IStringLocalizer<SharedResource> localizer,
        string channelName,
        bool isAnalyticOrDebug)
    {
        var message =
            localizer["Dashboard_EnableConfirm_Prompt", channelName] + "\n\n" +
            localizer["Dashboard_EnableConfirm_Body"];

        if (isAnalyticOrDebug)
        {
            message += "\n\n" + localizer["Dashboard_EnableConfirm_AnalyticNote"];
        }

        return dialog.ShowAlert(
            localizer["Dashboard_Alert_EnableLog"],
            message,
            localizer["Dashboard_EnableButton"],
            localizer["Modal_Cancel"]);
    }
}


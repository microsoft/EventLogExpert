// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.DetailsPane;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class DetailsPropertyLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, DetailsPropertyLabel label) => label switch
    {
        DetailsPropertyLabel.Source => localizer["Details_Property_Source"],
        DetailsPropertyLabel.DateTime => localizer["Details_Property_DateTime"],
        DetailsPropertyLabel.Computer => localizer["Details_Property_Computer"],
        DetailsPropertyLabel.LogName => localizer["Details_Property_LogName"],
        DetailsPropertyLabel.TaskCategory => localizer["Details_Property_TaskCategory"],
        DetailsPropertyLabel.Opcode => localizer["Details_Property_Opcode"],
        DetailsPropertyLabel.ResolutionStatus => localizer["Details_Property_ResolutionStatus"],
        DetailsPropertyLabel.Keywords => localizer["Details_Property_Keywords"],
        DetailsPropertyLabel.RecordId => localizer["Details_Property_RecordId"],
        DetailsPropertyLabel.ProcessId => localizer["Details_Property_ProcessId"],
        DetailsPropertyLabel.ThreadId => localizer["Details_Property_ThreadId"],
        DetailsPropertyLabel.ActivityId => localizer["Details_Property_ActivityId"],
        DetailsPropertyLabel.RelatedActivityId => localizer["Details_Property_RelatedActivityId"],
        DetailsPropertyLabel.User => localizer["Details_Property_User"],
        DetailsPropertyLabel.UserSid => localizer["Details_Property_UserSid"],
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, null)
    };
}

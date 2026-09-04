// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Common.Clipboard;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.LogTable;

internal sealed class EventCopyText(IStringLocalizer<SharedResource> localizer) : IEventCopyText
{
    public string MarkdownDescriptionHeader => localizer["Copy_Markdown_DescriptionHeader"];

    public string FieldLine(EventCopyFullField field, string value) => field switch
    {
        EventCopyFullField.LogName => localizer["Copy_Full_LogName", value],
        EventCopyFullField.Source => localizer["Copy_Full_Source", value],
        EventCopyFullField.Date => localizer["Copy_Full_Date", value],
        EventCopyFullField.EventId => localizer["Copy_Full_EventId", value],
        EventCopyFullField.TaskCategory => localizer["Copy_Full_TaskCategory", value],
        EventCopyFullField.Level => localizer["Copy_Full_Level", value],
        EventCopyFullField.Keywords => localizer["Copy_Full_Keywords", value],
        EventCopyFullField.User => localizer["Copy_Full_User", value],
        EventCopyFullField.UserSid => localizer["Copy_Full_UserSid", value],
        EventCopyFullField.Computer => localizer["Copy_Full_Computer", value],
        EventCopyFullField.DescriptionHeader => localizer["Copy_Full_DescriptionHeader"],
        EventCopyFullField.EventXmlHeader => localizer["Copy_Full_EventXmlHeader"],
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };
}
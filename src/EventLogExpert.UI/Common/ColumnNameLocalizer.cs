// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.LogTable;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class ColumnNameLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, ColumnName column) => column switch
    {
        ColumnName.RecordId => localizer["Column_RecordId"],
        ColumnName.Level => localizer["Column_Level"],
        ColumnName.DateAndTime => localizer["Column_DateAndTime"],
        ColumnName.ActivityId => localizer["Column_ActivityId"],
        ColumnName.Log => localizer["Column_Log"],
        ColumnName.ComputerName => localizer["Column_ComputerName"],
        ColumnName.Source => localizer["Column_Source"],
        ColumnName.EventId => localizer["Column_EventId"],
        ColumnName.TaskCategory => localizer["Column_TaskCategory"],
        ColumnName.Keywords => localizer["Column_Keywords"],
        ColumnName.ProcessId => localizer["Column_ProcessId"],
        ColumnName.ThreadId => localizer["Column_ThreadId"],
        ColumnName.User => localizer["Column_User"],
        ColumnName.Opcode => localizer["Column_Opcode"],
        _ => column.ToFullString()
    };
}

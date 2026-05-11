// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.UI.LogTable;

public enum ColumnName
{
    Level,
    [EnumMember(Value = "Date and Time")] DateAndTime,
    [EnumMember(Value = "Activity ID")] ActivityId,
    Log,
    [EnumMember(Value = "Computer Name")] ComputerName,
    Source,
    [EnumMember(Value = "Event ID")] EventId,
    [EnumMember(Value = "Task Category")] TaskCategory,
    Keywords,
    [EnumMember(Value = "Process ID")] ProcessId,
    [EnumMember(Value = "Thread ID")] ThreadId,
    User
}

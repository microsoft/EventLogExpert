// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.Filtering.Common.Filtering;

public enum EventProperty
{
    [EnumMember(Value = "Event ID")] Id,
    [EnumMember(Value = "Activity ID")] ActivityId,
    Level,
    Keywords,
    Source,
    [EnumMember(Value = "Task Category")] TaskCategory,
    [EnumMember(Value = "Process ID")] ProcessId,
    [EnumMember(Value = "Thread ID")] ThreadId,
    [EnumMember(Value = "User ID")] UserId,
    Description,
    Xml
}

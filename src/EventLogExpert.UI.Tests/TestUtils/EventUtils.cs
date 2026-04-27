// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class EventUtils
{
    internal static DisplayEventModel CreateTestEvent(
        int id = 1,
        string source = "TestSource",
        string level = "Information",
        string description = "Test description",
        string computerName = "TestComputer",
        string taskCategory = "TestCategory",
        string logName = "Application",
        DateTime? timeCreated = null,
        long? recordId = null,
        Guid? activityId = null,
        int? processId = null,
        int? threadId = null,
        IReadOnlyList<string>? keywords = null) =>
        new("TestLog", PathType.LogName)
        {
            Id = id,
            Source = source,
            Level = level,
            Description = description,
            ComputerName = computerName,
            TaskCategory = taskCategory,
            LogName = logName,
            TimeCreated = timeCreated ?? new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            RecordId = recordId,
            ActivityId = activityId,
            ProcessId = processId,
            ThreadId = threadId,
            Keywords = keywords ?? []
        };
}

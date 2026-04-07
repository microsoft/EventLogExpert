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
        string logName = "Application") =>
        new("TestLog", PathType.LogName)
        {
            Id = id,
            Source = source,
            Level = level,
            Description = description,
            ComputerName = computerName,
            TaskCategory = taskCategory,
            LogName = logName,
            TimeCreated = DateTime.Now
        };
}

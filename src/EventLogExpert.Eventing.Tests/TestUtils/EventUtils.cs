// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using static EventLogExpert.Eventing.Tests.TestUtils.Constants.Constants;

namespace EventLogExpert.Eventing.Tests.TestUtils;

public static class EventUtils
{
    public static EventRecord CreateBasicEvent() =>
        new()
        {
            RecordId = 1,
            ProviderName = TestProviderName,
            Id = 1000,
            ComputerName = LocalComputer,
            LogName = ApplicationLogName,
            TimeCreated = DateTime.UtcNow,
            Level = 3,
            ProcessId = 1234,
            ThreadId = 5678
        };

    public static IEnumerable<EventRecord> CreateDifferentEvents() =>
    [
        new()
        {
            RecordId = 1,
            ProviderName = TestProviderName,
            Id = 1000,
            ComputerName = LocalComputer,
            LogName = ApplicationLogName,
            TimeCreated = DateTime.UtcNow,
            Level = 3,
            ProcessId = 1234,
            ThreadId = 5678
        },
        new()
        {
            RecordId = 1,
            ProviderName = TestProviderName,
            Id = 1001,
            ComputerName = RemoteComputer,
            LogName = SystemLogName,
            TimeCreated = DateTime.UtcNow,
            Level = 1,
            ProcessId = 1234,
            ThreadId = 5678
        }
    ];

    // This event has a message in the legacy provider, but a task in the modern provider.
    public static EventRecord CreateExchangeEventRecord() =>
        new()
        {
            Id = 4114,
            Keywords = 36028797018963968,
            Level = 4,
            LogName = "Application",
            Properties = ["SERVER1", "4", "Lots of copy status text", "False"],
            ProviderName = "MSExchangeRepl",
            RecordId = 9518530,
            Task = 1,
            TimeCreated = DateTime.Parse("1/7/2023 10:02:00 AM"),
            ProcessId = 1234,
            ThreadId = 5678
        };

    public static ProviderDetails CreateExchangeProviderDetails() => new()
    {
        Events = [],
        Keywords = new Dictionary<long, string>(),
        Messages =
        [
            new MessageModel
            {
                LogLink = null,
                ProviderName = "MSExchangeRepl",
                RawId = 1074008082,
                ShortId = 4114,
                Tag = null,
                Template = null,
                Text = "Database redundancy health check passed.%nDatabase copy: %1%nRedundancy count: %2%nIsSuppressed: %4%n%nErrors:%n%3\r\n"
            }
        ],
        Opcodes = new Dictionary<int, string>(),
        ProviderName = "MSExchangeRepl",
        Tasks = new Dictionary<int, string>
        {
            { 1, "Service" },
            { 2, "Exchange VSS Writer" },
            { 3, "Move" },
            { 4, "Upgrade" },
            { 5, "Action" },
            { 6, "ExRes" }
        }
    };
}

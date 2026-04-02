// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
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
            TimeCreated = DateTime.UtcNow
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
            TimeCreated = DateTime.UtcNow
        },
        new()
        {
            RecordId = 1,
            ProviderName = TestProviderName,
            Id = 1001,
            ComputerName = RemoteComputer,
            LogName = SystemLogName,
            TimeCreated = DateTime.UtcNow
        }
    ];
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Components;

public partial class EventTable
{
    private readonly Dictionary<string, int> _colWidths = new()
    {
        { "RecordId", 10 },
        { "TimeCreated", 25 },
        { "Id", 10 },
        { "MachineName", 10 },
        { "Level", 15 },
        { "ProviderName", 25 },
        { "Task", 20 },
        { "Description", 200 }
    };
}

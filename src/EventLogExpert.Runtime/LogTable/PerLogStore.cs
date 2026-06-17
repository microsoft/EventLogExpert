// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable;

internal sealed record PerLogStore(
    EventLogId Id,
    string Name,
    LogPathType Type,
    RawEventList RawEvents,
    SegmentedSortedList DisplayList);

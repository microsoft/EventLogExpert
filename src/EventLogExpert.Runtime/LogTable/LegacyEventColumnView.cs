// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class LegacyEventColumnView(
    EventLogId logId,
    int generation,
    long contentVersion,
    IReadOnlyList<ResolvedEvent> events) : IEventColumnView
{
    private readonly LegacyEventColumnReader _reader =
        new LegacyEventColumnReader(logId, generation, contentVersion, events);

    public int Count => _reader.Count;

    public IEventColumnReader Reader => _reader;

    public ResolvedEvent GetDetail(EventHandle handle) => _reader.GetEvent(handle);

    public EventHandle HandleAt(int index) => _reader.HandleAt(index);

    public IReadOnlyList<ResolvedEvent> Slice(int start, int count) =>
        ResolvedEventIndex.Slice(_reader.Events, start, count);
}

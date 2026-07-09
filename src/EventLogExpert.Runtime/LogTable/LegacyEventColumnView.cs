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

    public ResolvedEvent GetDetail(EventLocator locator) => _reader.GetEvent(locator);

    public string GroupKeyAt(EventLocator locator, ColumnName column) =>
        ResolvedEventGroupKey.For(_reader, locator, column);

    public EventLocator LocatorAt(int index) => _reader.LocatorAt(index);

    public IReadOnlyList<ResolvedEvent> Slice(int start, int count) =>
        ResolvedEventIndex.Slice(_reader.Events, start, count);
}

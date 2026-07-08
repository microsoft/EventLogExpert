// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

internal interface IEventColumnView
{
    int Count { get; }

    IEventColumnReader Reader { get; }

    ResolvedEvent GetDetail(EventLocator locator);

    EventLocator LocatorAt(int index);

    IReadOnlyList<ResolvedEvent> Slice(int start, int count);
}

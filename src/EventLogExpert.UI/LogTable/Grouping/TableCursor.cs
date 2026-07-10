// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.LogTable.Grouping;

// Identity cursor for keyboard focus/nav: an event cursor carries the row's stable locator, a header
// cursor the stable group key. The visible row is derived on demand, never stored.
internal readonly record struct TableCursor(TableRowKind Kind, EventLocator? Handle, string? GroupKey)
{
    public static TableCursor ForEvent(EventLocator handle) => new(TableRowKind.Event, handle, null);

    public static TableCursor ForHeader(string groupKey) => new(TableRowKind.Header, null, groupKey);
}

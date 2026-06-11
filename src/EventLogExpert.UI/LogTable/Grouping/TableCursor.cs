// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.LogTable.Grouping;

// Identity cursor for keyboard focus/nav: an event cursor carries the event, a header
// cursor the stable group key. The visible row is derived on demand, never stored.
internal readonly record struct TableCursor(TableRowKind Kind, ResolvedEvent? Event, string? GroupKey)
{
    public static TableCursor ForEvent(ResolvedEvent @event) => new(TableRowKind.Event, @event, null);

    public static TableCursor ForHeader(string groupKey) => new(TableRowKind.Header, null, groupKey);
}

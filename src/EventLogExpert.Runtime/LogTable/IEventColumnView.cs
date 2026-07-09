// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

internal interface IEventColumnView
{
    int Count { get; }

    IEventColumnReader Reader { get; }

    ResolvedEvent GetDetail(EventLocator locator);

    /// <summary>
    ///     Full-lean single-row rehydrate (grid scalars plus Description) for the row addressed by
    ///     <paramref name="locator" />, used off the viewport slice path (for example a group header row).
    /// </summary>
    ResolvedEvent GetDetailLean(EventLocator locator);

    string GroupKeyAt(EventLocator locator, ColumnName column);

    EventLocator LocatorAt(int index);

    /// <summary>
    ///     The display position of <paramref name="locator" /> in this view, or <c>-1</c> when the locator is not in the
    ///     view (filtered out) or does not address this view's log generation.
    /// </summary>
    int Rank(EventLocator locator);

    IReadOnlyList<DisplayRow> Slice(int start, int count);
}

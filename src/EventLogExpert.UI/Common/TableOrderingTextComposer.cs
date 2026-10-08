// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.LogTable;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class TableOrderingTextComposer
{
    internal static string GroupAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        ColumnName column,
        bool isGroupDescending) =>
        localizer[
            isGroupDescending ? "Table_Announce_GroupedDescending" : "Table_Announce_GroupedAscending",
            ColumnNameLocalizer.Label(localizer, column)];

    internal static string GroupClearedAnnouncement(IStringLocalizer<SharedResource> localizer) =>
        localizer["Table_Announce_GroupCleared"];

    internal static string GroupedColumnDescription(IStringLocalizer<SharedResource> localizer, bool isGroupDescending) =>
        localizer[isGroupDescending ?
            "Table_GroupedColumnDescription_Descending" :
            "Table_GroupedColumnDescription_Ascending"];

    // One combined announcement for the rare flush that carries a change on BOTH axes (a persistent fault that
    // accumulated a sort change and a group change). The single-slot live region renders one message, so the two
    // fragments are composed into a single string instead of two announcements where the second would overwrite the
    // first before assistive technology reads it.
    internal static string SortAndGroupAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        ColumnName? sortColumn,
        bool sortDescending,
        ColumnName? groupColumn,
        bool groupDescending) =>
        localizer[
            "Table_Announce_SortedAndGrouped",
            sortColumn is { } column ? SortAnnouncement(localizer, column, sortDescending) : SortClearedAnnouncement(localizer),
            groupColumn is { } group ? GroupAnnouncement(localizer, group, groupDescending) : GroupClearedAnnouncement(localizer)];

    internal static string SortAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        ColumnName column,
        bool isDescending) =>
        localizer[
            isDescending ? "Table_Announce_SortedDescending" : "Table_Announce_SortedAscending",
            ColumnNameLocalizer.Label(localizer, column)];

    internal static string SortClearedAnnouncement(IStringLocalizer<SharedResource> localizer) =>
        localizer["Table_Announce_SortCleared"];
}

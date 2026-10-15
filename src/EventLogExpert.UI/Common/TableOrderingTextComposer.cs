// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.LogTable;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class TableOrderingTextComposer
{
    // Owns the Table_Announce_SortedAndGrouped composition for the table accessible descriptions: "{sort}. {group}" when
    // both axes are present, the lone phrase when one is, or null when neither is. Callers needing a non-null result
    // apply their own fallback. (The live SortAndGroupAnnouncement reuses the key with count-bearing fragments instead.)
    internal static string? CombineOrderingDescription(
        IStringLocalizer<SharedResource> localizer,
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending)
    {
        string? sortPhrase = orderBy is { } sort ? SortAnnouncement(localizer, sort, isDescending) : null;
        string? groupPhrase = groupBy is { } group ? GroupDirectionPhrase(localizer, group, isGroupDescending) : null;

        if (sortPhrase is not null && groupPhrase is not null)
        {
            return localizer["Table_Announce_SortedAndGrouped", sortPhrase, groupPhrase];
        }

        return groupPhrase ?? sortPhrase;
    }

    internal static string DefaultOrderReversedAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        bool isDescending,
        bool withinGroups) =>
        localizer[(withinGroups, isDescending) switch
        {
            (true, true) => "Table_Announce_WithinGroupOrderNewest",
            (true, false) => "Table_Announce_WithinGroupOrderOldest",
            (false, true) => "Table_Announce_DefaultOrderNewest",
            (false, false) => "Table_Announce_DefaultOrderOldest",
        }];

    internal static string GroupAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        ColumnName column,
        bool isGroupDescending,
        int groupCount)
    {
        string groupPhrase = GroupDirectionPhrase(localizer, column, isGroupDescending);
        string countPhrase = PluralText.Format(localizer, "Table_Announce_GroupCount", ("count", groupCount));

        return localizer["Table_Announce_GroupedWithCount", groupPhrase, countPhrase];
    }

    internal static string GroupClearedAnnouncement(IStringLocalizer<SharedResource> localizer) =>
        localizer["Table_Announce_GroupCleared"];

    internal static string GroupedColumnDescription(IStringLocalizer<SharedResource> localizer, bool isGroupDescending) =>
        localizer[isGroupDescending ?
            "Table_GroupedColumnDescription_Descending" :
            "Table_GroupedColumnDescription_Ascending"];

    // Composes the grouping phrase with the within-group reversal phrase so both land in the single live-region slot.
    internal static string GroupedWithWithinGroupOrderAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        ColumnName column,
        bool isGroupDescending,
        int groupCount,
        bool isDescending) =>
        localizer[
            "Table_Announce_GroupedWithWithinGroupOrder",
            GroupAnnouncement(localizer, column, isGroupDescending, groupCount),
            DefaultOrderReversedAnnouncement(localizer, isDescending, withinGroups: true)];

    // A reproject faulted and the grid reverted to the committed ordering; narrate what the user now sees using the
    // committed fields, NOT the requested ones (which the grid no longer reflects).
    internal static string ReorderUnavailableAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        ColumnName? committedOrderBy,
        bool committedIsDescending,
        ColumnName? committedGroupBy,
        bool committedIsGroupDescending) =>
        localizer[
            "Table_Announce_ReorderUnavailable",
            CommittedOrderDescription(localizer, committedOrderBy, committedIsDescending, committedGroupBy, committedIsGroupDescending)];

    // One combined announcement for the rare flush that carries a change on BOTH axes (a persistent fault that
    // accumulated a sort change and a group change). The single-slot live region renders one message, so the two
    // fragments are composed into a single string instead of two announcements where the second would overwrite the
    // first before assistive technology reads it.
    internal static string SortAndGroupAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        ColumnName? sortColumn,
        bool sortDescending,
        ColumnName? groupColumn,
        bool groupDescending,
        int groupCount) =>
        localizer[
            "Table_Announce_SortedAndGrouped",
            sortColumn is { } column ? SortAnnouncement(localizer, column, sortDescending) : SortClearedAnnouncement(localizer),
            groupColumn is { } group ? GroupAnnouncement(localizer, group, groupDescending, groupCount) : GroupClearedAnnouncement(localizer)];

    internal static string SortAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        ColumnName column,
        bool isDescending) =>
        localizer[
            isDescending ? "Table_Announce_SortedDescending" : "Table_Announce_SortedAscending",
            ColumnNameLocalizer.Label(localizer, column)];

    internal static string SortClearedAnnouncement(IStringLocalizer<SharedResource> localizer) =>
        localizer["Table_Announce_SortCleared"];

    private static string CommittedOrderDescription(
        IStringLocalizer<SharedResource> localizer,
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending) =>
        CombineOrderingDescription(localizer, orderBy, isDescending, groupBy, isGroupDescending) ??
        DefaultOrderReversedAnnouncement(localizer, isDescending, withinGroups: false);

    private static string GroupDirectionPhrase(
        IStringLocalizer<SharedResource> localizer,
        ColumnName column,
        bool isGroupDescending) =>
        localizer[
            isGroupDescending ? "Table_Announce_GroupedDescending" : "Table_Announce_GroupedAscending",
            ColumnNameLocalizer.Label(localizer, column)];
}

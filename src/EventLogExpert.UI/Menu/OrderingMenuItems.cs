// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.Common;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Menu;

// Shared builder for the mutually-exclusive Group By column picker, so the column-header menu and the View menu render
// the identical item set, radio semantics, and command path from one source.
internal static class OrderingMenuItems
{
    public static IReadOnlyList<MenuItem> BuildGroupBy(
        IStringLocalizer<SharedResource> localizer,
        IReadOnlyDictionary<ColumnName, bool> columns,
        ColumnName? requestedGroupBy,
        Action<ColumnName?> setGroupBy)
    {
        var items = new List<MenuItem>
        {
            MenuItem.Radio(
                localizer["LogTable_GroupByNone"].Value,
                () => setGroupBy(null),
                isChecked: requestedGroupBy is null)
        };

        foreach (var (column, _) in columns)
        {
            if (!ColumnDescriptors.IsGroupable(column)) { continue; }

            var capturedColumn = column;

            items.Add(MenuItem.Radio(
                ColumnNameLocalizer.Label(localizer, column),
                () => setGroupBy(capturedColumn),
                isChecked: requestedGroupBy.Equals(capturedColumn)));
        }

        return items;
    }
}

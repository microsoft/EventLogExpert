// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable.Grouping;

internal readonly record struct TableRow(TableRowKind Kind, int EventIndex, int GroupIndex)
{
    public static TableRow ForHeader(int groupIndex) => new(TableRowKind.Header, -1, groupIndex);

    public static TableRow ForEvent(int eventIndex, int groupIndex) => new(TableRowKind.Event, eventIndex, groupIndex);
}

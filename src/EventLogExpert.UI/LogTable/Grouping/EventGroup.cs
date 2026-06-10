// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable.Grouping;

internal readonly record struct EventGroup(
    string Key,
    int StartIndex,
    int EventCount,
    bool IsCollapsed,
    int VisibleStart)
{
    public int VisibleSize => IsCollapsed ? 1 : 1 + EventCount;
}

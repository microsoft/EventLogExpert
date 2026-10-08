// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Announcement;

public abstract record Announcement
{
    private protected Announcement() { }

    public sealed record Text(string Message) : Announcement;

    public sealed record LensKept(FilterLensLabel Label) : Announcement;

    public sealed record LensGroupSaved(string Name) : Announcement;

    public sealed record LensesSavedAll : Announcement;

    public sealed record FilterImportCompleted(ImportSummary Summary) : Announcement;

    public sealed record TagRemoved(string Tag, int Count) : Announcement;

    public sealed record TagRenamed(string OldTag, string NewTag, int Count) : Announcement;

    public sealed record TableSorted(ColumnName Column, bool IsDescending) : Announcement;

    public sealed record TableSortCleared : Announcement;

    public sealed record TableGrouped(ColumnName Column, bool IsGroupDescending) : Announcement;

    public sealed record TableGroupCleared : Announcement;

    public sealed record TableSortAndGroupChanged(
        ColumnName? SortColumn,
        bool SortDescending,
        ColumnName? GroupColumn,
        bool GroupDescending) : Announcement;
}

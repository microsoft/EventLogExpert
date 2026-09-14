// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;

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
}

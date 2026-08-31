// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;

namespace EventLogExpert.Runtime.Announcement;

public abstract record Announcement
{
    private protected Announcement() { }

    public sealed record Text(string Message) : Announcement;

    public sealed record LensKept(FilterLensLabel Label) : Announcement;
}

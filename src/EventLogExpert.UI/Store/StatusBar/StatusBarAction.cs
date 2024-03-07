// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Store.StatusBar;

public sealed record StatusBarAction
{
    public sealed record ClearStatus(Guid ActivityId);

    public sealed record CloseAll;

    /// <summary>Used to indicate the progress of event logs being loaded.</summary>
    /// <param name="ActivityId">
    ///     A unique id that distinguishes this loading activity from others, since log names such as
    ///     Application will be common and many file names will be the same.
    /// </param>
    /// <param name="Count"></param>
    public sealed record SetEventsLoading(Guid ActivityId, int Count);

    public sealed record SetResolverStatus(string ResolverStatus);
}

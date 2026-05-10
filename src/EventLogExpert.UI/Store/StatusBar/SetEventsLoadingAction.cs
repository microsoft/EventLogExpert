// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Store.StatusBar;

/// <summary>Used to indicate the progress of event logs being loaded.</summary>
/// <param name="ActivityId">
///     A unique id that distinguishes this loading activity from others, since log names such as
///     Application will be common and many file names will be the same.
/// </param>
/// <param name="Count"></param>
public sealed record SetEventsLoadingAction(Guid ActivityId, int Count, int FailedCount);

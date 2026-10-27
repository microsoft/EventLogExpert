// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.StatusBar;

/// <param name="ActivityId">The loading activity that finished.</param>
/// <param name="FinalFailedCount">
///     The activity's failed count when it finished, which can exceed the last progress report.
/// </param>
public sealed record ClearStatusAction(StatusActivityId ActivityId, int FinalFailedCount);

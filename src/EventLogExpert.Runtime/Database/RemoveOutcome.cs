// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database;

public readonly record struct RemoveOutcome(
    RemoveOutcomeStatus Status,
    bool Removed,
    bool LogsReopened)
{
    public static RemoveOutcome NotFound { get; } = new(RemoveOutcomeStatus.NotFound, false, false);

    public static RemoveOutcome NotConfirmed { get; } = new(RemoveOutcomeStatus.NotConfirmed, false, false);

    public bool Confirmed => Status == RemoveOutcomeStatus.Confirmed;
}

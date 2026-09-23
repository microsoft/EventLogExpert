// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.Common.Operations;

public sealed record DatabaseToolsResult(DatabaseToolsOutcome Outcome, string? FailureSummary, TimeSpan Duration)
{
    public bool SummaryIsDiagnostic { get; init; }
}

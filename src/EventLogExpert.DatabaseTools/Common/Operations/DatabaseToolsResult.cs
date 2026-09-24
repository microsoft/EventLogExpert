// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.DatabaseTools.Common.Operations;

public sealed record DatabaseToolsResult(DatabaseToolsOutcome Outcome, LocalizableText? Summary, TimeSpan Duration)
{
    public bool SummaryIsDiagnostic { get; init; }

    public string? DiagnosticDetail { get; init; }
}

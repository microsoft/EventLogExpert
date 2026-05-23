// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.Contracts;

/// <summary>
///     Final result of a DatabaseTools operation. Logs and progress stream separately via
///     <see cref="System.IProgress{T}" /> sinks while the operation runs; this record carries only the terminal outcome,
///     an optional failure summary (set when <see cref="Outcome" /> is <see cref="DatabaseToolsOutcome.Failed" />), and
///     the measured duration.
/// </summary>
public sealed record DatabaseToolsResult(DatabaseToolsOutcome Outcome, string? FailureSummary, TimeSpan Duration);

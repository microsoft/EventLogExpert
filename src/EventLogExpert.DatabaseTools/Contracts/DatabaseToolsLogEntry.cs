// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.DatabaseTools.Contracts;

/// <summary>One log line emitted by a DatabaseTools operation. The UI renders each entry with severity-based formatting.</summary>
/// <param name="TimestampUtc">When the entry was emitted (captured at handler-call time, not materialization).</param>
/// <param name="Level">Severity level.</param>
/// <param name="Message">Materialized message text from the interpolated-string handler.</param>
public sealed record DatabaseToolsLogEntry(DateTime TimestampUtc, LogLevel Level, string Message);

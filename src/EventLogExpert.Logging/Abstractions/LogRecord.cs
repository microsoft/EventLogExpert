// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Abstractions;

/// <summary>One log line emitted by an <see cref="ITraceLogger" /> sink, carrying severity and materialized text.</summary>
/// <param name="TimestampUtc">When the entry was emitted (captured at handler-call time, not materialization).</param>
/// <param name="Level">Severity level.</param>
/// <param name="Message">Materialized message text from the interpolated-string handler.</param>
public sealed record LogRecord(DateTime TimestampUtc, LogLevel Level, string Message);

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Abstractions;

public sealed record LogRecord(
    DateTime TimestampUtc,
    LogLevel Level,
    string Message,
    string Category = "",
    ProcessOrigin ProcessOrigin = ProcessOrigin.InProcess);

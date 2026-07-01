// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.DebugLog;

public sealed record DebugLogEntry(
    DateTimeOffset? Timestamp,
    int? ThreadId,
    LogLevel? Level,
    int MessageStartIndex,
    string RawLine,
    string? Category = null,
    ProcessOrigin? ProcessOrigin = null)
{
    public string Message => RawLine[MessageStartIndex..];
}

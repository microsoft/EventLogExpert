// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.DebugLog;

public sealed record DebugLogEntry(
    DateTimeOffset? Timestamp,
    int? ThreadId,
    LogLevel? Level,
    int MessageStartIndex,
    string RawLine)
{
    public string Message => RawLine[MessageStartIndex..];
}

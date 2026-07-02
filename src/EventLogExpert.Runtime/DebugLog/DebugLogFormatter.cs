// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Runtime.DebugLog;

/// <summary>
///     Writes the debug-log line format that <see cref="DebugLogEntryParser" /> reads back. The two MUST stay in sync:
///     <c>
///         [&lt;local o-timestamp&gt;] [&lt;threadId&gt;] [&lt;Level&gt;]( [&lt;category&gt;])?( [ElevatedHelper])? &lt;
///         message&gt;
///     </c>
///     . Invoked synchronously on the emitting thread by
///     <see cref="EventLogExpert.Logging.Sinks.FileLogSink" />, so <see cref="Environment.CurrentManagedThreadId" />
///     reflects the caller.
/// </summary>
internal static class DebugLogFormatter
{
    public static string Format(LogRecord record)
    {
        string categoryTag = string.IsNullOrEmpty(record.Category) ? string.Empty : $"[{record.Category}] ";
        string originTag = record.ProcessOrigin == ProcessOrigin.ElevatedHelper ? "[ElevatedHelper] " : string.Empty;
        string message = EscapeLeadingBracket(record.Message);

        return $"[{record.TimestampUtc.ToLocalTime():o}] [{Environment.CurrentManagedThreadId}] [{record.Level}] {categoryTag}{originTag}{message}";
    }

    // A message starting with '[' would otherwise be read back as a category/origin tag; a leading '\' escapes it (and
    // itself) so DebugLogEntryParser.TryParseLine can strip exactly one '\' and recover the original text.
    internal static string EscapeLeadingBracket(string message) =>
        !string.IsNullOrEmpty(message) && message[0] is '[' or '\\' ? '\\' + message : message;
}

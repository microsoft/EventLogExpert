// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EventLogExpert.Runtime.DebugLog;

/// <summary>
///     Parses lines written by <see cref="DebugLogFormatter" /> into structured <see cref="DebugLogEntry" /> records.
///     The writer formats each entry as
///     <c>[{DateTime.Now:o}] [{Environment.CurrentManagedThreadId}] [{level}] {message}</c>. Lines whose prefix does not
///     parse fully are treated as continuations of the previous entry (e.g., subsequent lines of a multi-line stack trace)
///     or, if there is no previous entry, as standalone entries with all metadata fields set to null.
/// </summary>
internal static class DebugLogEntryParser
{
    private static readonly Regex s_linePrefixRegex = BuildLinePrefixRegex();

    /// <summary>
    ///     Parses a complete sequence of log lines into entries. Continuation lines fold into the previous entry's
    ///     <see cref="DebugLogEntry.Message" /> and <see cref="DebugLogEntry.RawLine" /> joined by <c>\n</c>. A continuation
    ///     line with no preceding entry becomes a standalone entry with <see cref="DebugLogEntry.Level" />,
    ///     <see cref="DebugLogEntry.Timestamp" />, and <see cref="DebugLogEntry.ThreadId" /> all set to null.
    /// </summary>
    public static IReadOnlyList<DebugLogEntry> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var entries = new List<DebugLogEntry>();
        var parser = new DebugLogEntryStreamParser();

        foreach (var line in lines)
        {
            var emitted = parser.AddLine(line);

            if (emitted is not null) { entries.Add(emitted); }
        }

        var final = parser.Flush();

        if (final is not null) { entries.Add(final); }

        return entries;
    }

    /// <summary>
    ///     Attempts to parse a single line as a new entry start. Returns true and emits the parsed entry only when all
    ///     three prefix fields (timestamp, thread id, level) parse successfully. The emitted entry's
    ///     <see cref="DebugLogEntry.RawLine" /> equals <paramref name="line" /> except when the formatter escaped a message
    ///     beginning with <c>[</c> or <c>\</c> (see <see cref="DebugLogFormatter.EscapeLeadingBracket" />): the one leading
    ///     escape <c>\</c> is stripped so RawLine carries the real text. A legacy pre-escape log whose message starts with
    ///     <c>\</c> therefore renders one <c>\</c> short until the log rotates - display-only, no on-disk change.
    /// </summary>
    public static bool TryParseLine(string line, [NotNullWhen(true)] out DebugLogEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(line);

        var match = s_linePrefixRegex.Match(line);

        if (!match.Success ||
            !DateTimeOffset.TryParseExact(match.Groups["ts"].Value,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp) ||
            !int.TryParse(match.Groups["tid"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var threadId) ||
            !Enum.TryParse<LogLevel>(match.Groups["level"].Value, true, out var level))
        {
            entry = null;

            return false;
        }

        int messageStart = match.Groups["message"].Index;
        string rawLine = line;

        // Un-escape a message the formatter escaped (leading '\' before a '[' or '\'); RawLine is what projection,
        // copy/export, and filtering read, so the real text must live there, with MessageStartIndex unchanged.
        if (messageStart < line.Length && line[messageStart] == '\\')
        {
            rawLine = string.Concat(line.AsSpan(0, messageStart), line.AsSpan(messageStart + 1));
        }

        entry = new DebugLogEntry(
            timestamp,
            threadId,
            level,
            messageStart,
            rawLine,
            match.Groups["category"].Success ? match.Groups["category"].Value : null,
            match.Groups["origin"].Success ? ProcessOrigin.ElevatedHelper : ProcessOrigin.InProcess);

        return true;
    }

    private static Regex BuildLinePrefixRegex()
    {
        string roots = string.Join('|', LogCategories.KnownRoots.Select(Regex.Escape));

        return new Regex(
            $@"^\[(?<ts>[^\]]+)\] \[(?<tid>\d+)\] \[(?<level>[A-Za-z]+)\](?: \[(?<category>(?:{roots})(?:\.[A-Za-z0-9]+)*)\])?(?: \[(?<origin>ElevatedHelper)\])? (?<message>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}

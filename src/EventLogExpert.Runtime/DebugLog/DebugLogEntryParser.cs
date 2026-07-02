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
public static class DebugLogEntryParser
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
    ///     <see cref="DebugLogEntry.RawLine" /> equals <paramref name="line" />.
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

        entry = new DebugLogEntry(
            timestamp,
            threadId,
            level,
            match.Groups["message"].Index,
            line,
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

/// <summary>
///     Stateful streaming parser for <see cref="DebugLogFormatter" /> output. Emits a completed
///     <see cref="DebugLogEntry" /> from <see cref="AddLine" /> the moment a new entry header arrives (with all preceding
///     continuation lines folded into the previous entry's <see cref="DebugLogEntry.Message" /> and
///     <see cref="DebugLogEntry.RawLine" />). Call <see cref="Flush" /> after the final line to drain any remaining
///     pending entry.
/// </summary>
/// <remarks>
///     This type exists so callers reading from an asynchronous source (e.g.,
///     <see cref="IDebugLogReader.LoadAsync" />) can render entries incrementally rather than buffering the entire stream
///     and parsing in one shot. The non-streaming <see cref="DebugLogEntryParser.Parse(IEnumerable{string})" /> overload
///     is implemented in terms of this type.
/// </remarks>
public sealed class DebugLogEntryStreamParser
{
    private List<string>? _continuationLines;
    private DebugLogEntry? _pending;

    /// <summary>
    ///     Feeds one line into the parser. Returns the previously buffered entry (with its continuation lines folded in)
    ///     when <paramref name="line" /> starts a new entry; otherwise returns <c>null</c> and buffers the line internally.
    ///     Any line whose prefix parses successfully starts a new entry, regardless of timestamp ordering relative to the
    ///     pending entry (older releases formatted timestamps before acquiring the writer lock, so persisted logs may
    ///     legitimately contain headers in non-chronological order).
    /// </summary>
    public DebugLogEntry? AddLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (DebugLogEntryParser.TryParseLine(line, out var parsed))
        {
            var emitted = BuildPending();

            _pending = parsed;
            _continuationLines = null;

            return emitted;
        }

        if (_pending is null)
        {
            _pending = new DebugLogEntry(null, null, null, 0, line);

            return null;
        }

        _continuationLines ??= [];
        _continuationLines.Add(line);

        return null;
    }

    /// <summary>
    ///     Returns the final pending entry (with continuation lines folded in) and resets state. Returns <c>null</c> when
    ///     no entry is currently buffered.
    /// </summary>
    public DebugLogEntry? Flush()
    {
        var emitted = BuildPending();

        _pending = null;
        _continuationLines = null;

        return emitted;
    }

    private DebugLogEntry? BuildPending()
    {
        if (_pending is null) { return null; }

        if (_continuationLines is not { Count: > 0 }) { return _pending; }

        var joinedContinuations = string.Join('\n', _continuationLines);

        _pending = _pending with { RawLine = _pending.RawLine + '\n' + joinedContinuations };

        return _pending;
    }
}

/// <summary>
///     Streaming parser for <see cref="DebugLogFormatter" /> output read NEWEST-first (see
///     <see cref="IDebugLogReader.LoadAsync" />). Because the lines arrive reversed, every continuation line of an entry
///     is seen BEFORE that entry's header, so a header line completes an entry immediately - there is no
///     pending-until-the-next-header state. Emitted <see cref="DebugLogEntry" /> records are byte-identical to
///     <see cref="DebugLogEntryStreamParser" />'s; only the emission order is reversed.
/// </summary>
public sealed class DebugLogEntryReverseStreamParser
{
    private readonly List<string> _continuationLines = [];

    /// <summary>
    ///     Feeds one line arriving newest-first. Returns a completed entry when <paramref name="line" /> is an entry
    ///     header (its already-buffered continuation lines folded back into file order); otherwise buffers the line as a
    ///     continuation and returns <c>null</c>.
    /// </summary>
    public DebugLogEntry? AddLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (DebugLogEntryParser.TryParseLine(line, out var header))
        {
            var emitted = FoldBufferedContinuations(header);
            _continuationLines.Clear();

            return emitted;
        }

        _continuationLines.Add(line);

        return null;
    }

    /// <summary>
    ///     Returns the file's leading orphan continuation lines (those before the first header) as one standalone
    ///     null-metadata entry - matching the forward parser - and resets state. Returns <c>null</c> when none remain.
    /// </summary>
    public DebugLogEntry? Flush()
    {
        if (_continuationLines.Count == 0) { return null; }

        var orphan = new DebugLogEntry(null, null, null, 0, JoinBufferedInFileOrder());
        _continuationLines.Clear();

        return orphan;
    }

    private DebugLogEntry FoldBufferedContinuations(DebugLogEntry header)
    {
        // A header-only entry has no buffered continuations; leave its RawLine untouched (mirrors the forward
        // BuildPending guard) so projection does not later split a spurious trailing empty line off "header\n".
        if (_continuationLines.Count == 0) { return header; }

        return header with { RawLine = header.RawLine + '\n' + JoinBufferedInFileOrder() };
    }

    // The continuations arrived newest-first, so walk the buffer in reverse to restore file order before joining.
    private string JoinBufferedInFileOrder()
    {
        var fileOrder = new string[_continuationLines.Count];

        for (var i = 0; i < _continuationLines.Count; i++)
        {
            fileOrder[i] = _continuationLines[_continuationLines.Count - 1 - i];
        }

        return string.Join('\n', fileOrder);
    }
}

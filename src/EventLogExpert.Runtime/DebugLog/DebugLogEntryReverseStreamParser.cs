// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DebugLog;

/// <summary>
///     Streaming parser for <see cref="DebugLogFormatter" /> output read NEWEST-first (see
///     <see cref="IDebugLogReader.LoadAsync" />). Because the lines arrive reversed, every continuation line of an entry
///     is seen BEFORE that entry's header, so a header line completes an entry immediately - there is no
///     pending-until-the-next-header state. Emitted <see cref="DebugLogEntry" /> records are byte-identical to
///     <c>DebugLogEntryStreamParser</c>'s; only the emission order is reversed.
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

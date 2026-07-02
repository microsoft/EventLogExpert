// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DebugLog;

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
internal sealed class DebugLogEntryStreamParser
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

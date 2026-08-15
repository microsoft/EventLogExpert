// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Filtering.Evaluation;

/// <summary>
///     The per-log result of an on-demand XML filter evaluation: a match bitset over the reader's row-index space
///     plus the snapshot stamp (generation, content version, count) it was computed against.
/// </summary>
public sealed class XmlFilterMatch
{
    private readonly bool[] _matches;

    public XmlFilterMatch(EventLogId logId, int generation, long contentVersion, int count, bool[] matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentOutOfRangeException.ThrowIfNotEqual(matches.Length, count);

        LogId = logId;
        Generation = generation;
        ContentVersion = contentVersion;
        Count = count;
        _matches = matches;
    }

    public long ContentVersion { get; }

    public int Count { get; }

    public int Generation { get; }

    public EventLogId LogId { get; }

    /// <summary>
    ///     <c>true</c> when <paramref name="locator" /> addresses a matching row of this snapshot; a locator from a
    ///     different log or generation, or an out-of-range index, is not a match.
    /// </summary>
    public bool IsMatch(EventLocator locator) =>
        locator.LogId == LogId &&
        locator.Generation == Generation &&
        (uint)locator.Index < (uint)Count &&
        _matches[locator.Index];
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Filtering.Evaluation;

/// <summary>
///     The per-log result of an on-demand XML filter evaluation: a membership bitset over the reader's row-index
///     space plus the snapshot stamp (generation, content version, count) it was computed against.
/// </summary>
internal sealed class XmlFilterMembership
{
    private readonly bool[] _membership;

    internal XmlFilterMembership(EventLogId logId, int generation, long contentVersion, int count, bool[] membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        LogId = logId;
        Generation = generation;
        ContentVersion = contentVersion;
        Count = count;
        _membership = membership;
    }

    public long ContentVersion { get; }

    public int Count { get; }

    public int Generation { get; }

    public EventLogId LogId { get; }

    /// <summary>
    ///     <c>true</c> when <paramref name="locator" /> addresses a member of this snapshot; a locator from a different
    ///     log or generation, or an out-of-range index, is not a member.
    /// </summary>
    public bool IsMember(EventLocator locator) =>
        locator.LogId == LogId &&
        locator.Generation == Generation &&
        (uint)locator.Index < (uint)Count &&
        _membership[locator.Index];
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Evaluation;

namespace EventLogExpert.Runtime.EventLog;

/// <summary>
///     Holds the on-demand XML filter matches for the currently applied filter, keyed by log, so the ordered-view and
///     presence gates can survive rows without reopening a log with pre-rendered XML.
/// </summary>
internal sealed class XmlFilterMatchCache
{
    private readonly Lock _gate = new();

    private Dictionary<EventLogId, XmlFilterMatch> _byLog = new();
    private Filter? _filter;
    private long _publishedSequence;
    private long _sequenceCounter;

    public void Clear()
    {
        lock (_gate)
        {
            _filter = null;
            _byLog = new Dictionary<EventLogId, XmlFilterMatch>();
        }
    }

    public XmlFilterMatch? GetMatch(Filter filter, EventLogId logId)
    {
        lock (_gate)
        {
            return _filter is { } stored &&
                !filter.HasFilteringChangedFrom(stored) &&
                _byLog.TryGetValue(logId, out XmlFilterMatch? match)
                    ? match
                    : null;
        }
    }

    public long NextSequence() => Interlocked.Increment(ref _sequenceCounter);

    public void Remove(EventLogId logId)
    {
        lock (_gate)
        {
            _byLog.Remove(logId);
        }
    }

    public void RemoveNotIn(IReadOnlySet<EventLogId> openLogIds)
    {
        ArgumentNullException.ThrowIfNull(openLogIds);

        lock (_gate)
        {
            foreach (EventLogId logId in _byLog.Keys.Where(id => !openLogIds.Contains(id)).ToList())
            {
                _byLog.Remove(logId);
            }
        }
    }

    public bool Set(Filter filter, Dictionary<EventLogId, XmlFilterMatch> byLog, long sequence)
    {
        ArgumentNullException.ThrowIfNull(byLog);

        lock (_gate)
        {
            if (sequence < _publishedSequence) { return false; }

            _publishedSequence = sequence;
            _filter = filter;
            _byLog = byLog;

            return true;
        }
    }
}

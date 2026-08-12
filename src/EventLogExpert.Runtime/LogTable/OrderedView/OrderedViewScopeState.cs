// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class OrderedViewScopeState
{
    private readonly Dictionary<LogGeneration, int> _coverage = [];

    private readonly Dictionary<EventLogId, int> _highestGenerationSeen = [];
    private readonly HashSet<EventLogId> _removedLogs = [];
    private readonly HashSet<EventLogId> _scopeLogs = [];

    private bool _scoped;

    public IEnumerable<LogGeneration> Keys => _coverage.Keys;

    public long ScopeVersion { get; private set; }

    public EventLogId? SingleLog
    {
        get
        {
            if (!_scoped || _scopeLogs.Count != 1) { return null; }

            foreach (EventLogId logId in _scopeLogs) { return logId; }

            return null;
        }
    }

    public void AdvanceCoverage(in LogGeneration key, int coveredCount)
    {
        if (coveredCount > _coverage.GetValueOrDefault(key)) { _coverage[key] = coveredCount; }
    }

    public int Coverage(in LogGeneration key) => _coverage.GetValueOrDefault(key);

    public void EvictOutOfScope(in FrozenScope scope, IReadOnlyDictionary<EventLogId, int> activeGeneration)
    {
        foreach ((EventLogId logId, int active) in activeGeneration)
        {
            if (!scope.Includes(logId)) { RecordGenerationSeen(logId, active); }
        }

        List<LogGeneration>? evicted = null;

        foreach (LogGeneration key in _coverage.Keys)
        {
            bool outOfScope = !scope.Includes(key.LogId);
            bool closedGeneration = activeGeneration.TryGetValue(key.LogId, out int active) && key.Generation < active;

            if (outOfScope || closedGeneration) { (evicted ??= []).Add(key); }
        }

        if (evicted is null) { return; }

        foreach (LogGeneration key in evicted)
        {
            RecordGenerationSeen(key.LogId, key.Generation);
            _coverage.Remove(key);
        }
    }

    public FrozenScope Freeze()
    {
        HashSet<EventLogId>? scope = _scoped ? [.. _scopeLogs] : null;

        return new FrozenScope(scope, [.. _removedLogs]);
    }

    public RowCoverage FreezeCoverage() => new(new Dictionary<LogGeneration, int>(_coverage));

    public bool Includes(EventLogId logId) =>
        !_removedLogs.Contains(logId) && (!_scoped || _scopeLogs.Contains(logId));

    public bool IsAtOrAboveGenerationFloor(EventLogId logId, int generation) =>
        !_highestGenerationSeen.TryGetValue(logId, out int floor) || generation >= floor;

    public void RecordGenerationSeen(EventLogId logId, int generation)
    {
        if (_removedLogs.Contains(logId)) { return; }

        if (generation > _highestGenerationSeen.GetValueOrDefault(logId)) { _highestGenerationSeen[logId] = generation; }
    }

    public void Remove(EventLogId logId)
    {
        _removedLogs.Add(logId);
        _scopeLogs.Remove(logId);
        _highestGenerationSeen.Remove(logId);
        DropCoverage(logId);
    }

    public void Reset()
    {
        _coverage.Clear();
        _highestGenerationSeen.Clear();
        _removedLogs.Clear();
        _scopeLogs.Clear();
        _scoped = true;
    }

    public bool ScopeEquals(IReadOnlyCollection<EventLogId> scopeLogs)
    {
        if (!_scoped) { return false; }

        int matched = 0;

        foreach (EventLogId logId in scopeLogs)
        {
            if (!_scopeLogs.Contains(logId)) { return false; }

            matched++;
        }

        return matched == _scopeLogs.Count;
    }

    public bool TrySetScope(IReadOnlyCollection<EventLogId> scopeLogs, long scopeVersion)
    {
        if (scopeVersion < ScopeVersion) { return false; }

        _scoped = true;
        ScopeVersion = scopeVersion;
        _scopeLogs.Clear();

        foreach (EventLogId logId in scopeLogs)
        {
            if (!_removedLogs.Contains(logId)) { _scopeLogs.Add(logId); }
        }

        return true;
    }

    private void DropCoverage(EventLogId logId)
    {
        List<LogGeneration>? drop = null;

        foreach (LogGeneration key in _coverage.Keys)
        {
            if (key.LogId == logId) { (drop ??= []).Add(key); }
        }

        if (drop is null) { return; }

        foreach (LogGeneration key in drop) { _coverage.Remove(key); }
    }
}

internal readonly struct FrozenScope
{
    private readonly HashSet<EventLogId>? _scopeLogs;
    private readonly HashSet<EventLogId> _removedLogs;

    internal FrozenScope(HashSet<EventLogId>? scopeLogs, HashSet<EventLogId> removedLogs)
    {
        _scopeLogs = scopeLogs;
        _removedLogs = removedLogs;
    }

    public int LogCount => _scopeLogs?.Count ?? 0;

    public bool Includes(EventLogId logId) =>
        !_removedLogs.Contains(logId) && (_scopeLogs is null || _scopeLogs.Contains(logId));
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using Fluxor;

namespace EventLogExpert.Runtime.ActivityCorrelation;

internal sealed class ActivityCorrelationService(IState<RawEventStoreState> rawStore)
    : IActivityCorrelationService, IActivityCorrelationCacheControl
{
    private const int MaxEventsPerActivity = 200;
    private const int SharedActivityThreshold = 500;
    private const int SharedPreviewCap = 25;

    private readonly Lock _cacheGate = new();
    private readonly IState<RawEventStoreState> _rawStore = rawStore;

    private CachedView? _cache;

    public async Task<ActivityCorrelationView?> BuildAsync(EventLocator focusedEvent, CancellationToken cancellationToken)
    {
        var byLog = _rawStore.Value.ByLog;

        if (!byLog.TryGetValue(focusedEvent.LogId, out var store)) { return null; }

        // A locator from a superseded generation, or one that no longer addresses a row, cannot be correlated against
        // the current snapshot. ContentVersion is folded into the freshness token so a same-generation replace still
        // rebuilds rather than resolving stale content.
        if (focusedEvent.Generation != store.Generation ||
            focusedEvent.Index < 0 ||
            focusedEvent.Index >= store.Count)
        {
            return null;
        }

        var token = new CorrelationContentToken(focusedEvent.LogId, store.Generation, store.ContentVersion, store.Count);

        lock (_cacheGate)
        {
            if (_cache is { } cached && cached.Focused == focusedEvent && cached.Token == token)
            {
                return cached.View;
            }
        }

        var view = await Task.Run(() => BuildCore(store, focusedEvent, token, cancellationToken), cancellationToken);

        // Cache only if the snapshot is still current: a close or content change during the build must not repopulate
        // the neighborhood of a log that is no longer open. TryGetContentToken reads the live store, and the check runs
        // under the same gate as Invalidate, so a close landing during the build always wins.
        lock (_cacheGate)
        {
            if (TryGetContentToken(focusedEvent.LogId, out var current) && current == token)
            {
                _cache = new CachedView(focusedEvent, token, view);
            }
        }

        return view;
    }

    public void Invalidate()
    {
        lock (_cacheGate) { _cache = null; }
    }

    public bool TryGetContentToken(EventLogId logId, out CorrelationContentToken token)
    {
        if (_rawStore.Value.ByLog.TryGetValue(logId, out var store))
        {
            token = new CorrelationContentToken(logId, store.Generation, store.ContentVersion, store.Count);

            return true;
        }

        token = default;

        return false;
    }

    private static void AddRow(Dictionary<Guid, List<int>> rowsByActivity, Guid activity, int row)
    {
        if (!rowsByActivity.TryGetValue(activity, out var rows))
        {
            rows = [];
            rowsByActivity[activity] = rows;
        }

        rows.Add(row);
    }

    private static ActivityCorrelationView BuildCore(
        EventColumnStore store,
        EventLocator focused,
        CorrelationContentToken token,
        CancellationToken cancellationToken)
    {
        var reader = store.CreateReader(focused.LogId);
        int count = reader.Count;

        var activityIds = new Guid[count];
        var activityHas = new bool[count];
        reader.CopyGuidColumn(EventFieldId.ActivityId, activityIds, activityHas);

        Guid focusActivity = activityHas[focused.Index] ? activityIds[focused.Index] : Guid.Empty;

        if (focusActivity == Guid.Empty)
        {
            return new ActivityCorrelationView { LogId = focused.LogId, FocusActivityId = Guid.Empty, Token = token };
        }

        var relatedIds = new Guid[count];
        var relatedHas = new bool[count];
        reader.CopyGuidColumn(EventFieldId.RelatedActivityId, relatedIds, relatedHas);

        // Pooled Level codes for a topology-only severity tally (no string materialization); -1 marks an absent level.
        var levelPoolIndex = new int[count];
        reader.CopyPoolIndexColumn(EventFieldId.Level, levelPoolIndex);
        var pool = reader.Pool;
        var severityByPoolIndex = new Dictionary<int, SeverityLevel?>();

        var memberRows = new List<int>();
        var parentsOfFocus = new HashSet<Guid>();
        var childActivities = new HashSet<Guid>();

        for (int i = 0; i < count; i++)
        {
            if ((i & 0xFFFF) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            bool hasActivity = activityHas[i];
            Guid activity = hasActivity ? activityIds[i] : Guid.Empty;

            if (hasActivity && activity == focusActivity)
            {
                memberRows.Add(i);

                if (relatedHas[i] && relatedIds[i] != Guid.Empty && relatedIds[i] != focusActivity)
                {
                    parentsOfFocus.Add(relatedIds[i]);
                }
            }
            else if (relatedHas[i] && relatedIds[i] == focusActivity && hasActivity && activity != Guid.Empty)
            {
                // Record the child's identity; the full-membership pass below gathers every event of that activity,
                // since typically only the child's first (transfer) event carries the RelatedActivityId link.
                childActivities.Add(activity);
            }
        }

        var parentRowsByActivity = new Dictionary<Guid, List<int>>();
        var childRowsByActivity = new Dictionary<Guid, List<int>>();

        // Second pass: gather ALL rows of each parent and child activity (not just the linking rows), so a node's count,
        // span, tallies, events, and false-fusion flag reflect the activity's full membership.
        if (parentsOfFocus.Count > 0 || childActivities.Count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                if ((i & 0xFFFF) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                if (!activityHas[i]) { continue; }

                Guid activity = activityIds[i];

                if (activity == focusActivity) { continue; }

                if (parentsOfFocus.Contains(activity)) { AddRow(parentRowsByActivity, activity, i); }

                if (childActivities.Contains(activity)) { AddRow(childRowsByActivity, activity, i); }
            }
        }

        // An activity that is both a parent and a child of the focus is a 2-cycle; surface it once (as a flagged parent).
        var cyclicActivities = new HashSet<Guid>(parentsOfFocus);
        cyclicActivities.IntersectWith(childActivities);

        var focusNode = BuildNode(
            reader,
            SeverityOfRow,
            focusActivity,
            ActivityNodeRole.Focus,
            memberRows,
            [.. parentsOfFocus],
            pinnedRow: focused.Index,
            isCycle: false,
            cancellationToken);

        var parentNodes = new List<ActivityNode>();

        foreach (var parent in parentsOfFocus)
        {
            var rows = parentRowsByActivity.GetValueOrDefault(parent) ?? [];

            parentNodes.Add(BuildNode(
                reader,
                SeverityOfRow,
                parent,
                ActivityNodeRole.Parent,
                rows,
                [],
                pinnedRow: null,
                isCycle: cyclicActivities.Contains(parent),
                cancellationToken));
        }

        var childNodes = new List<ActivityNode>();

        foreach (var child in childActivities.Where(activity => !cyclicActivities.Contains(activity)))
        {
            childNodes.Add(BuildNode(
                reader,
                SeverityOfRow,
                child,
                ActivityNodeRole.Child,
                childRowsByActivity[child],
                [focusActivity],
                pinnedRow: null,
                isCycle: false,
                cancellationToken));
        }

        // Newest activity first; a referenced-but-absent parent (MaxTicks 0) sorts last.
        parentNodes.Sort(static (left, right) => right.MaxTicks.CompareTo(left.MaxTicks));
        childNodes.Sort(static (left, right) => right.MaxTicks.CompareTo(left.MaxTicks));

        var nodes = new List<ActivityNode>(1 + parentNodes.Count + childNodes.Count) { focusNode };
        nodes.AddRange(parentNodes);
        nodes.AddRange(childNodes);

        return new ActivityCorrelationView
        {
            LogId = focused.LogId,
            FocusActivityId = focusActivity,
            Token = token,
            Activities = nodes,
            HasHierarchy = parentsOfFocus.Count > 0 || childActivities.Count > 0
        };

        SeverityLevel? SeverityOfRow(int row)
        {
            int poolIndex = levelPoolIndex[row];

            if (poolIndex < 0) { return null; }

            if (!severityByPoolIndex.TryGetValue(poolIndex, out var severity))
            {
                severity = LevelSeverity.FromLevelName(poolIndex < pool.Count ? pool[poolIndex] : null);
                severityByPoolIndex[poolIndex] = severity;
            }

            return severity;
        }
    }

    private static ActivityNode BuildNode(
        IEventColumnReader reader,
        Func<int, SeverityLevel?> severityOfRow,
        Guid activityId,
        ActivityNodeRole role,
        List<int> rows,
        IReadOnlyList<Guid> parents,
        int? pinnedRow,
        bool isCycle,
        CancellationToken cancellationToken)
    {
        int eventCount = rows.Count;
        bool oversized = eventCount > SharedActivityThreshold;
        int cap = oversized ? SharedPreviewCap : MaxEventsPerActivity;

        long minTicks = long.MaxValue;
        long maxTicks = long.MinValue;
        int critical = 0;
        int error = 0;
        int warning = 0;

        // Reserve one display slot for the selected (pinned) event so it is never capped out.
        int heapCap = pinnedRow is null ? cap : Math.Max(cap - 1, 0);

        // A bounded min-heap of size heapCap keeps the highest-priority rows and evicts the lowest on overflow. Priority
        // (Rank, Ticks, Row): for an oversized node Critical/Error rows (Rank 1) outrank non-errors (Rank 0), then newer
        // outranks older, then higher row index. So the heap retains errors and the newest events, evicting
        // non-errors/oldest/lowest-index first.
        var heap = new PriorityQueue<RowInfo, (int Rank, long Ticks, int Row)>(heapCap + 1);
        RowInfo? pinned = null;

        for (int k = 0; k < eventCount; k++)
        {
            if ((k & 0xFFFF) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            int row = rows[k];
            long ticks = reader.GetTimeTicks(reader.LocatorAt(row));

            if (ticks < minTicks) { minTicks = ticks; }

            if (ticks > maxTicks) { maxTicks = ticks; }

            var severity = severityOfRow(row);

            switch (severity)
            {
                case SeverityLevel.Critical: critical++; break;
                case SeverityLevel.Error: error++; break;
                case SeverityLevel.Warning: warning++; break;
            }

            if (row == pinnedRow)
            {
                pinned = new RowInfo(row, ticks);

                continue;
            }

            if (heapCap == 0) { continue; }

            int rank = oversized && severity is SeverityLevel.Critical or SeverityLevel.Error ? 1 : 0;
            heap.Enqueue(new RowInfo(row, ticks), (rank, ticks, row));

            if (heap.Count > heapCap) { heap.Dequeue(); }
        }

        var chosen = new List<RowInfo>(heap.Count + 1);

        while (heap.Count > 0) { chosen.Add(heap.Dequeue()); }

        if (pinned is { } pin) { chosen.Add(pin); }

        // Render newest-first to match the table's default sort.
        chosen.Sort(static (left, right) =>
        {
            int byTime = right.Ticks.CompareTo(left.Ticks);

            return byTime != 0 ? byTime : right.Row.CompareTo(left.Row);
        });

        var events = new List<CorrelatedEvent>(chosen.Count);

        foreach (var candidate in chosen)
        {
            events.Add(new CorrelatedEvent(reader.LocatorAt(candidate.Row), candidate.Ticks));
        }

        return new ActivityNode
        {
            ActivityId = activityId,
            Role = role,
            EventCount = eventCount,
            MinTicks = eventCount == 0 ? 0 : minTicks,
            MaxTicks = eventCount == 0 ? 0 : maxTicks,
            IsSharedOversized = oversized,
            Parents = parents,
            IsCycle = isCycle,
            CriticalCount = critical,
            ErrorCount = error,
            WarningCount = warning,
            Events = events,
            EventsTruncated = eventCount > events.Count
        };
    }

    private readonly record struct RowInfo(int Row, long Ticks);

    private sealed record CachedView(EventLocator Focused, CorrelationContentToken Token, ActivityCorrelationView View);
}

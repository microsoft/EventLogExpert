// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

// Randomized reducer ops checked against the array-of-structs oracle. The live path is now the columnar view, so the
// oracle is the retained AoS sort and parity is asserted by VALUE identity (the view rehydrates fresh objects from
// columns) over count, display order, Rank, Slice, and ResolveByKey.
public sealed class LogTableReducerDifferentialTests
{
    [Fact]
    public void DisplayedEvents_IsIdentityStable_UntilPerLogEventsChange()
    {
        var harness = new Harness(seed: 5);
        harness.AppendToAnyLog();

        var state = harness.State;

        // Same PerLogEvents identity -> same combined instance.
        Assert.Same(state.DisplayedEvents, state.DisplayedEvents);

        var resorted = Reducers.ReduceToggleGroupSorting(state); // no GroupBy -> no-op, returns same state
        Assert.Same(state, resorted);

        var requested = Reducers.ReduceSetOrderBy(state, new SetOrderByAction(ColumnName.Level));
        Assert.Same(state.PerLogEvents, requested.PerLogEvents);
        Assert.Equal(state.DisplayListVersion + 1, requested.DisplayListVersion);

        var afterOrderBy = Settle(requested);
        Assert.NotSame(state.PerLogEvents, afterOrderBy.PerLogEvents);
        Assert.NotSame(state.DisplayedEvents, afterOrderBy.DisplayedEvents);

        harness.AppendToAnyLog();
        Assert.NotSame(state.DisplayedEvents, harness.State.DisplayedEvents);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(101)]
    [InlineData(2026)]
    [InlineData(31337)]
    [InlineData(8675309)]
    [InlineData(int.MaxValue)]
    public void Reducers_KeepDisplayedAndPerLogViewsMatchingTheOracle_OverRandomOps(int seed)
    {
        var harness = new Harness(seed);

        harness.Run(operationCount: 300);
    }

    private static LogTableState Settle(LogTableState state)
    {
        // The off-thread rebuild re-sorts every per-log view to the requested context and republishes; mirror that by
        // healing the stored views to the requested context and dispatching DisplayReady.
        var context = state.SortContext;
        var views = new Dictionary<EventLogId, EventColumnView>(state.PerLogEvents.Count);

        foreach (var (logId, view) in state.PerLogEvents)
        {
            views[logId] = view.WithContext(context);
        }

        return Reducers.ReduceDisplayReady(
            state,
            new DisplayReadyAction { Views = views, Version = state.DisplayListVersion });
    }

    private sealed class Harness
    {
        private static readonly string[] s_levels = ["Information", "Warning", "Error", "Critical"];
        private static readonly string[] s_sources = ["Provider.A", "Provider.B", "Provider.C"];
        private static readonly string[] s_tasks = ["", "Logon", "Service"];

        private readonly ColumnName?[] _groupBys = [null, ColumnName.Source, ColumnName.Level, ColumnName.EventId];
        private readonly List<LogModel> _logs = [];
        private readonly ColumnName?[] _orderBys =
            [null, ColumnName.DateAndTime, ColumnName.Level, ColumnName.Source, ColumnName.EventId, ColumnName.TaskCategory];
        private readonly Random _rng;

        public Harness(int seed) => _rng = new Random(seed);

        public LogTableState State { get; private set; } = new();

        private ColumnName? EffectiveOrderBy =>
            ResolvedEventOrdering.ResolveDefaultOrderBy(State.OrderBy, State.GroupBy, OpenLogs().Count, State.TimelineVisible);

        public void AppendToAnyLog()
        {
            var log = OpenLogs().FirstOrDefault() ?? AddLog();
            AppendBatch([(log, GenerateBatch(log, 1 + _rng.Next(20)))]);
        }

        public void Run(int operationCount)
        {
            for (int step = 0; step < operationCount; step++)
            {
                ApplyRandomOperation();
                AssertDisplayedMatchesOracle(step);
                AssertPerLogViewsMatchOracle(step);
                AssertRankMatchesOracle(step);
                AssertSliceMatchesOracle(step);
                AssertResolveByKeyRoundTrips(step);
            }
        }

        private static bool SameEvent(ResolvedEvent expected, ResolvedEvent actual) =>
            expected.RecordId == actual.RecordId
            && expected.Id == actual.Id
            && expected.TimeCreated == actual.TimeCreated
            && expected.LogPathType == actual.LogPathType
            && string.Equals(expected.OwningLog, actual.OwningLog, StringComparison.Ordinal)
            && string.Equals(expected.Level, actual.Level, StringComparison.Ordinal)
            && string.Equals(expected.Source, actual.Source, StringComparison.Ordinal)
            && string.Equals(expected.TaskCategory, actual.TaskCategory, StringComparison.Ordinal);

        private LogModel AddLog()
        {
            var data = new EventLogData($"Log{_logs.Count}", LogPathType.Channel);
            var model = new LogModel(data.Id, data.Name, new DateTime(2026, 1, 1).AddDays(_logs.Count));
            _logs.Add(model);
            State = Reducers.ReduceAddTable(State, new AddTableAction(data));

            return model;
        }

        private void AppendBatch(IReadOnlyList<(LogModel Log, IReadOnlyList<ResolvedEvent> Events)> batch)
        {
            var byLog = new Dictionary<EventLogId, EventColumnView>(batch.Count);

            foreach (var (log, events) in batch)
            {
                log.Events.AddRange(events);

                // Production rebuilds the full view over the whole raw store per append; mirror that with the full model.
                byLog[log.Id] = DisplayViewTestFactory.Build(log.Id, log.Events);
            }

            State = Reducers.ReduceAppendTableEventsBatch(State, new AppendTableEventsBatchAction { ViewsByLog = byLog });
        }

        private void AppendSingle(LogModel log)
        {
            var events = GenerateBatch(log, 1 + _rng.Next(20));
            log.Events.AddRange(events);

            State = Reducers.ReduceAppendTableEvents(
                State,
                new AppendTableEventsAction(log.Id) { View = DisplayViewTestFactory.Build(log.Id, log.Events) });
        }

        private void ApplyRandomOperation()
        {
            var open = OpenLogs();
            int roll = _rng.Next(100);

            if (open.Count == 0 || (roll < 16 && _logs.Count(l => l.IsOpen) < 4))
            {
                var log = AddLog();
                AppendBatch([(log, GenerateBatch(log, 1 + _rng.Next(30)))]);
            }
            else if (roll < 40)
            {
                int logCount = 1 + _rng.Next(open.Count);
                var chosen = open.OrderBy(_ => _rng.Next()).Take(logCount)
                    .Select(log => (log, (IReadOnlyList<ResolvedEvent>)GenerateBatch(log, 1 + _rng.Next(30)))).ToList();
                AppendBatch(chosen);
            }
            else if (roll < 50)
            {
                AppendSingle(open[_rng.Next(open.Count)]);
            }
            else if (roll < 60)
            {
                Finalize(open[_rng.Next(open.Count)]);
            }
            else if (roll < 70)
            {
                ReapplyFilter(open);
            }
            else if (roll < 76)
            {
                // A batch addressed to an already-closed log must be dropped, not resurrected.
                StaleAppendToClosedLog(open);
            }
            else if (roll < 83)
            {
                ApplySort(Reducers.ReduceSetOrderBy(State, new SetOrderByAction(_orderBys[_rng.Next(_orderBys.Length)])));
            }
            else if (roll < 89)
            {
                ApplySort(Reducers.ReduceSetGroupBy(State, new SetGroupByAction(_groupBys[_rng.Next(_groupBys.Length)])));
            }
            else if (roll < 93)
            {
                ApplySort(Reducers.ReduceToggleGroupSorting(State));
            }
            else if (roll < 97)
            {
                ApplySort(Reducers.ReduceToggleSorting(State));
            }
            else
            {
                // May close the last open log (the K->0 reconciliation branch).
                CloseLog(open[_rng.Next(open.Count)]);
            }
        }

        private void ApplySort(LogTableState requested)
        {
            State = requested.DisplayListVersion != State.DisplayListVersion ? Settle(requested) : requested;
        }

        private void AssertDisplayedMatchesOracle(int step)
        {
            var oracle = Oracle();
            var displayed = State.DisplayedEvents;

            Assert.True(
                oracle.Count == displayed.Count,
                $"Displayed count mismatch at step {step}: oracle={oracle.Count} displayed={displayed.Count} ({Describe()}).");

            var rows = displayed.Slice(0, displayed.Count);

            for (int i = 0; i < oracle.Count; i++)
            {
                Assert.True(
                    SameEvent(oracle[i], rows[i].Lean),
                    $"Displayed order mismatch at step {step}, index {i} ({Describe()}).");
            }
        }

        private void AssertPerLogViewsMatchOracle(int step)
        {
            foreach (var log in OpenLogs())
            {
                var oracle = AosReferenceOrdering.OrderedEvents(
                    log.Events, EffectiveOrderBy, State.IsDescending, State.GroupBy, State.IsGroupDescending);
                var view = State.EventsForLog(log.Id);

                Assert.True(
                    oracle.Count == view.Count,
                    $"Per-log count mismatch at step {step} for {log.Name}: oracle={oracle.Count} view={view.Count}.");

                var rows = view.Slice(0, view.Count);

                for (int i = 0; i < oracle.Count; i++)
                {
                    Assert.True(
                        SameEvent(oracle[i], rows[i].Lean),
                        $"Per-log order mismatch at step {step}, {log.Name}[{i}] ({Describe()}).");
                }
            }
        }

        private void AssertRankMatchesOracle(int step)
        {
            var displayed = State.DisplayedEvents;

            if (displayed.Count == 0) { return; }

            var rows = displayed.Slice(0, displayed.Count);

            foreach (int index in SampleIndices(displayed.Count))
            {
                int rank = displayed.Rank(rows[index].Loc);

                Assert.True(rank == index, $"Rank mismatch at step {step}: expected {index} got {rank} ({Describe()}).");
            }
        }

        private void AssertResolveByKeyRoundTrips(int step)
        {
            var displayed = State.DisplayedEvents;

            if (displayed.Count == 0) { return; }

            var rows = displayed.Slice(0, displayed.Count);

            foreach (int index in SampleIndices(displayed.Count))
            {
                var row = rows[index];

                // The corpus uses non-null RecordIds, so every row carries a stable key that must re-resolve to its
                // own locator, the value-key restore path across a reload.
                Assert.True(ValueKey.TryCreate(row.Lean, out var key), $"Row missing a key at step {step}, index {index}.");
                Assert.Equal(row.Loc, displayed.ResolveByKey(key));
            }
        }

        private void AssertSliceMatchesOracle(int step)
        {
            var oracle = Oracle();
            var displayed = State.DisplayedEvents;

            foreach (var (start, count) in SampleWindows(oracle.Count))
            {
                var slice = displayed.Slice(start, count);
                int expected = start >= oracle.Count ? 0 : Math.Min(count, oracle.Count - start);

                Assert.True(
                    slice.Count == expected,
                    $"Slice length mismatch at step {step}, window ({start},{count}): expected {expected} got {slice.Count}.");

                for (int i = 0; i < slice.Count; i++)
                {
                    Assert.True(
                        SameEvent(oracle[start + i], slice[i].Lean),
                        $"Slice element mismatch at step {step}, window ({start},{count})[{i}] ({Describe()}).");
                }
            }
        }

        private void CloseLog(LogModel log)
        {
            log.IsOpen = false;
            State = Reducers.ReduceCloseLog(State, new CloseLogAction(log.Id));
        }

        private string Describe() =>
            $"orderBy={State.OrderBy?.ToString() ?? "null"} desc={State.IsDescending} " +
            $"groupBy={State.GroupBy?.ToString() ?? "null"} groupDesc={State.IsGroupDescending} logs={OpenLogs().Count}";

        private void Finalize(LogModel log)
        {
            log.Events.Clear();
            log.Events.AddRange(GenerateBatch(log, 1 + _rng.Next(60)));

            State = Reducers.ReduceUpdateTable(
                State,
                new UpdateTableAction(log.Id)
                {
                    View = DisplayViewTestFactory.Build(log.Id, log.Events),
                    Version = State.DisplayListVersion
                });
        }

        private List<ResolvedEvent> GenerateBatch(LogModel log, int count)
        {
            var batch = new List<ResolvedEvent>(count);

            for (int i = 0; i < count; i++)
            {
                log.LastTime = log.LastTime.AddMilliseconds(1 + _rng.Next(1000));

                batch.Add(new ResolvedEvent(log.Name, LogPathType.Channel)
                {
                    RecordId = log.NextRecordId++,
                    TimeCreated = log.LastTime,
                    Id = 1000 + _rng.Next(6),
                    Level = s_levels[_rng.Next(s_levels.Length)],
                    Source = s_sources[_rng.Next(s_sources.Length)],
                    TaskCategory = s_tasks[_rng.Next(s_tasks.Length)]
                });
            }

            return batch;
        }

        private List<LogModel> OpenLogs() => _logs.Where(log => log.IsOpen).ToList();

        private IReadOnlyList<ResolvedEvent> Oracle() =>
            AosReferenceOrdering.OrderedEvents(
                OpenLogs().SelectMany(log => log.Events),
                EffectiveOrderBy, State.IsDescending, State.GroupBy, State.IsGroupDescending);

        private void ReapplyFilter(IReadOnlyList<LogModel> open)
        {
            var activeLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(open.Count);

            foreach (var log in open)
            {
                // Omit a log to exercise the preserve (stale-snapshot) branch.
                if (_rng.Next(4) == 0) { continue; }

                var kept = log.Events.Where(_ => _rng.Next(3) != 0).ToList();
                log.Events.Clear();
                log.Events.AddRange(kept);
                activeLogs[log.Id] = kept;
            }

            var context = State.SortContext;
            var views = new Dictionary<EventLogId, EventColumnView>(activeLogs.Count);

            foreach (var (logId, events) in activeLogs)
            {
                views[logId] = DisplayViewTestFactory.Build(logId, events, context);
            }

            State = Reducers.ReduceDisplayReady(
                State,
                new DisplayReadyAction { Views = views, Version = State.DisplayListVersion });
        }

        private IEnumerable<int> SampleIndices(int count)
        {
            foreach (int candidate in new[] { 0, count / 2, count - 1 })
            {
                if (candidate >= 0 && candidate < count) { yield return candidate; }
            }

            for (int i = 0; i < 5; i++) { yield return _rng.Next(count); }
        }

        private IEnumerable<(int Start, int Count)> SampleWindows(int total)
        {
            yield return (0, 0);
            yield return (0, total);

            if (total == 0)
            {
                yield return (0, 4); // overrun on an empty view -> empty slice
                yield break;
            }

            yield return (0, Math.Min(8, total));
            yield return (Math.Max(0, total - 5), 5);            // tail, may overrun -> clamped
            yield return (total, 3);                             // start at end -> empty
            yield return (_rng.Next(total), 1 + _rng.Next(16));  // random window, may overrun -> clamped
        }

        private void StaleAppendToClosedLog(IReadOnlyList<LogModel> open)
        {
            var closed = _logs.Where(log => !log.IsOpen).ToList();

            if (closed.Count == 0)
            {
                AppendSingle(open[_rng.Next(open.Count)]); // nothing closed yet; keep the op productive
                return;
            }

            // Ghost events: not added to the model; the reducer drops them.
            var target = closed[_rng.Next(closed.Count)];
            var ghosts = GenerateBatch(target, 1 + _rng.Next(10));

            State = Reducers.ReduceAppendTableEventsBatch(State, new AppendTableEventsBatchAction
            {
                ViewsByLog = new Dictionary<EventLogId, EventColumnView>
                {
                    [target.Id] = DisplayViewTestFactory.Build(target.Id, ghosts)
                }
            });
        }

        private sealed class LogModel(EventLogId id, string name, DateTime startTime)
        {
            public List<ResolvedEvent> Events { get; } = [];

            public EventLogId Id { get; } = id;

            public bool IsOpen { get; set; } = true;

            public DateTime LastTime { get; set; } = startTime;

            public string Name { get; } = name;

            public long NextRecordId { get; set; } = 1;
        }
    }
}

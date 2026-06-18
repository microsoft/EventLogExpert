// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

// Randomized reducer ops checked against a naive re-sorted oracle by reference.
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

        // A sort-context change must mint a fresh PerLogEvents.
        var afterOrderBy = Reducers.ReduceSetOrderBy(state, new SetOrderByAction(ColumnName.Level));
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
            ResolvedEventOrdering.ResolveDefaultOrderBy(State.OrderBy, State.GroupBy, OpenLogs().Count);

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
            var byLog = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(batch.Count);

            foreach (var (log, events) in batch)
            {
                byLog[log.Id] = events;
                log.Events.AddRange(events);
            }

            State = Reducers.ReduceAppendTableEventsBatch(State, new AppendTableEventsBatchAction(byLog));
        }

        private void AppendSingle(LogModel log)
        {
            var events = GenerateBatch(log, 1 + _rng.Next(20));
            log.Events.AddRange(events);

            State = Reducers.ReduceAppendTableEvents(State, new AppendTableEventsAction(log.Id, events));
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
                State = Reducers.ReduceSetOrderBy(State, new SetOrderByAction(_orderBys[_rng.Next(_orderBys.Length)]));
            }
            else if (roll < 89)
            {
                State = Reducers.ReduceSetGroupBy(State, new SetGroupByAction(_groupBys[_rng.Next(_groupBys.Length)]));
            }
            else if (roll < 93)
            {
                State = Reducers.ReduceToggleGroupSorting(State);
            }
            else if (roll < 97)
            {
                State = Reducers.ReduceToggleSorting(State);
            }
            else
            {
                // May close the last open log (the K->0 reconciliation branch).
                CloseLog(open[_rng.Next(open.Count)]);
            }
        }

        private void AssertDisplayedMatchesOracle(int step)
        {
            var oracle = Oracle();
            var displayed = State.DisplayedEvents;

            Assert.True(
                oracle.Count == displayed.Count,
                $"Displayed count mismatch at step {step}: oracle={oracle.Count} displayed={displayed.Count} ({Describe()}).");

            for (int i = 0; i < oracle.Count; i++)
            {
                Assert.True(
                    ReferenceEquals(oracle[i], displayed[i]),
                    $"Displayed order mismatch at step {step}, index {i} ({Describe()}).");
            }
        }

        private void AssertPerLogViewsMatchOracle(int step)
        {
            foreach (var log in OpenLogs())
            {
                var oracle = log.Events.SortEvents(
                    EffectiveOrderBy, State.IsDescending, State.GroupBy, State.IsGroupDescending);
                var view = State.EventsForLog(log.Id);

                Assert.True(
                    oracle.Count == view.Count,
                    $"Per-log count mismatch at step {step} for {log.Name}: oracle={oracle.Count} view={view.Count}.");

                for (int i = 0; i < oracle.Count; i++)
                {
                    Assert.True(
                        ReferenceEquals(oracle[i], view[i]),
                        $"Per-log order mismatch at step {step}, {log.Name}[{i}] ({Describe()}).");
                }
            }
        }

        private void AssertRankMatchesOracle(int step)
        {
            var displayed = State.DisplayedEvents;

            if (displayed.Count == 0) { return; }

            foreach (int index in SampleIndices(displayed.Count))
            {
                int rank = ResolvedEventIndex.IndexOf(
                    displayed, displayed[index], State.OrderBy, State.IsDescending, State.GroupBy, State.IsGroupDescending);

                Assert.True(rank == index, $"Rank mismatch at step {step}: expected {index} got {rank} ({Describe()}).");
            }
        }

        private void AssertResolveByKeyRoundTrips(int step)
        {
            var displayed = State.DisplayedEvents;

            if (displayed.Count == 0) { return; }

            foreach (int index in SampleIndices(displayed.Count))
            {
                var original = displayed[index];

                Assert.Same(original, ResolvedEventIndex.ResolveByKey(displayed, original));

                // Key path: a stale clone resolves to the live instance.
                Assert.Same(original, ResolvedEventIndex.ResolveByKey(displayed, original with { }));
            }
        }

        private void AssertSliceMatchesOracle(int step)
        {
            var oracle = Oracle();
            var displayed = State.DisplayedEvents;

            foreach (var (start, count) in SampleWindows(oracle.Count))
            {
                var slice = ResolvedEventIndex.Slice(displayed, start, count);
                int expected = start >= oracle.Count ? 0 : Math.Min(count, oracle.Count - start);

                Assert.True(
                    slice.Count == expected,
                    $"Slice length mismatch at step {step}, window ({start},{count}): expected {expected} got {slice.Count}.");

                for (int i = 0; i < slice.Count; i++)
                {
                    Assert.True(
                        ReferenceEquals(slice[i], oracle[start + i]),
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

            State = Reducers.ReduceUpdateTable(State, new UpdateTableAction(log.Id, [.. log.Events]));
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
            OpenLogs()
                .SelectMany(log => log.Events)
                .SortEvents(EffectiveOrderBy, State.IsDescending, State.GroupBy, State.IsGroupDescending);

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
            var lists = new Dictionary<EventLogId, SegmentedSortedList>(activeLogs.Count);

            foreach (var (logId, events) in activeLogs)
            {
                lists[logId] = SegmentedSortedList.CreateSorted(events, context);
            }

            State = Reducers.ReduceDisplayReady(State, new DisplayReadyAction { Lists = lists });
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

            State = Reducers.ReduceAppendTableEventsBatch(State, new AppendTableEventsBatchAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [target.Id] = ghosts }));
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

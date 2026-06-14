// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class MergeDifferentialTests
{
    private static readonly ColumnName?[] s_groupBys =
    [
        null,
        ColumnName.Source,
        ColumnName.Level,
        ColumnName.EventId
    ];

    private static readonly ColumnName?[] s_orderBys =
    [
        null,
        ColumnName.DateAndTime,
        ColumnName.Level,
        ColumnName.Source,
        ColumnName.EventId,
        ColumnName.Keywords,
        ColumnName.Log,
        ColumnName.User,
        ColumnName.ComputerName,
        ColumnName.TaskCategory,
        ColumnName.ProcessId,
        ColumnName.ThreadId,
        ColumnName.ActivityId
    ];

    [Fact]
    public void AppendingEmptyBatch_LeavesSequenceUnchanged()
    {
        var harness = new Harness(seed: 5);
        harness.AppendToActiveLog(count: 25);

        var before = harness.GoldenSnapshot();
        harness.AppendBatch([]);

        Assert.Equal(before, harness.GoldenSnapshot());
    }

    [Fact]
    public void ClosingDownToSingleLog_MatchesNaiveRecompute()
    {
        var harness = new Harness(seed: 9);

        for (int i = 0; i < 4; i++) { harness.AppendToNewLog(count: 30); }

        harness.ResortRandom();
        harness.CloseLogsUntilOneRemains();

        harness.AssertGoldenMatchesNaive();
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
    public void IncrementalMergeAndFilter_MatchNaiveRecompute_OverRandomOps(int seed)
    {
        var harness = new Harness(seed);

        harness.Run(operationCount: 400);
    }

    private sealed class Harness
    {
        private static readonly string[] s_computers = ["HOST-1", "HOST-2"];

        private static readonly IReadOnlyList<string>[] s_keywords =
        [
            [], ["Audit Success"], ["Classic"], ["Audit Failure", "Classic"]
        ];
        private static readonly string[] s_levels = ["Information", "Warning", "Error", "Critical", "Verbose"];

        private static readonly SecurityIdentifier?[] s_sids =
        [
            null,
            new SecurityIdentifier("S-1-5-18"),
            new SecurityIdentifier("S-1-5-19"),
            new SecurityIdentifier("S-1-5-21-1004336348-1177238915-682003330-1001")
        ];
        private static readonly string[] s_sources = ["Provider.A", "Provider.B", "Provider.C", "Provider.D"];
        private static readonly string[] s_tasks = ["", "Logon", "Service", "Network"];
        private readonly List<LogModel> _logs = [];

        private readonly Random _rng;

        private IReadOnlyList<ResolvedEvent> _golden = [];
        private ColumnName? _groupBy;
        private bool _isDescending = true;
        private bool _isGroupDescending;

        private ColumnName? _orderBy;

        public Harness(int seed) => _rng = new Random(seed);

        private ColumnName EffectiveOrderBy => ResolvedEventOrdering.GetEffectiveOrderBy(_orderBy);

        public void AppendBatch(IReadOnlyList<ResolvedEvent> batch) =>
            _golden = ResolvedEventOrdering.MergeSorted(_golden, batch, EffectiveOrderBy, _isDescending, _groupBy, _isGroupDescending);

        public void AppendToActiveLog(int count)
        {
            var log = ActiveLogs().FirstOrDefault() ?? ActivateNewLog();
            AppendBatch(GenerateBatch(log, count));
        }

        public void AppendToNewLog(int count) => AppendBatch(GenerateBatch(ActivateNewLog(), count));

        public void AssertGoldenMatchesNaive(int step = -1)
        {
            var naive = LiveEvents().SortEvents(EffectiveOrderBy, _isDescending, _groupBy, _isGroupDescending);

            Assert.True(
                _golden.Count == naive.Count,
                $"Count mismatch at step {step}: golden={_golden.Count} naive={naive.Count} (ctx {DescribeContext()}).");

            for (int i = 0; i < naive.Count; i++)
            {
                Assert.True(
                    ReferenceEquals(_golden[i], naive[i]),
                    $"Order mismatch at step {step}, index {i} (ctx {DescribeContext()}).");
            }
        }

        public void CloseLogsUntilOneRemains()
        {
            while (ActiveLogs().Count > 1)
            {
                CloseLog(ActiveLogs()[0]);
            }
        }

        public IReadOnlyList<ResolvedEvent> GoldenSnapshot() => [.. _golden];

        public void ResortRandom()
        {
            PickRandomSort();
            _golden = _golden.SortEvents(EffectiveOrderBy, _isDescending, _groupBy, _isGroupDescending);
        }

        public void Run(int operationCount)
        {
            for (int step = 0; step < operationCount; step++)
            {
                ApplyRandomOperation();
                AssertGoldenSorted(step);
                AssertGoldenMatchesNaive(step);
            }
        }

        private static IReadOnlyList<ResolvedEvent> FilterOutOwningLog(IReadOnlyList<ResolvedEvent> events, string owningLog)
        {
            if (events is SegmentedSortedList segmented)
            {
                return segmented.WhereSegmented(e => !string.Equals(e.OwningLog, owningLog, StringComparison.Ordinal));
            }

            var filtered = new List<ResolvedEvent>(events.Count);

            for (int i = 0; i < events.Count; i++)
            {
                if (!string.Equals(events[i].OwningLog, owningLog, StringComparison.Ordinal))
                {
                    filtered.Add(events[i]);
                }
            }

            return filtered.AsReadOnly();
        }

        private LogModel ActivateNewLog()
        {
            var inactive = _logs.FirstOrDefault(l => !l.IsOpen && l.Events.Count == 0);

            if (inactive is not null)
            {
                inactive.IsOpen = true;

                return inactive;
            }

            var log = new LogModel($"Log{_logs.Count}", new DateTime(2026, 1, 1).AddDays(_logs.Count));
            _logs.Add(log);

            return log;
        }

        private List<LogModel> ActiveLogs() => _logs.Where(l => l.IsOpen).ToList();

        private void ApplyRandomOperation()
        {
            var active = ActiveLogs();
            int roll = _rng.Next(100);

            if (active.Count == 0 || (roll < 20 && _logs.Count < 4))
            {
                AppendBatch(GenerateBatch(ActivateNewLog(), 1 + _rng.Next(40)));
            }
            else if (roll < 60)
            {
                AppendBatch(GenerateBatch(active[_rng.Next(active.Count)], 1 + _rng.Next(40)));
            }
            else if (roll < 75)
            {
                ResortRandom();
            }
            else if (roll < 88)
            {
                FinalizeLog(active[_rng.Next(active.Count)]);
            }
            else if (active.Count > 1)
            {
                CloseLog(active[_rng.Next(active.Count)]);
            }
            else
            {
                AppendBatch(GenerateBatch(active[_rng.Next(active.Count)], 1 + _rng.Next(40)));
            }
        }

        private void AssertGoldenSorted(int step)
        {
            var comparer = ResolvedEventOrdering.SelectComparer(EffectiveOrderBy, _isDescending, _groupBy, _isGroupDescending);

            for (int i = 1; i < _golden.Count; i++)
            {
                Assert.True(
                    comparer(_golden[i - 1], _golden[i]) <= 0,
                    $"Golden not sorted at step {step}, index {i} (ctx {DescribeContext()}).");
            }
        }

        private void CloseLog(LogModel log)
        {
            log.IsOpen = false;
            _golden = FilterOutOwningLog(_golden, log.Name);
        }

        private string DescribeContext() =>
            $"orderBy={_orderBy?.ToString() ?? "null"} desc={_isDescending} groupBy={_groupBy?.ToString() ?? "null"} groupDesc={_isGroupDescending}";

        private void FinalizeLog(LogModel log)
        {
            var withoutLog = FilterOutOwningLog(_golden, log.Name);

            _golden = ResolvedEventOrdering.MergeSorted(
                withoutLog, [.. log.Events], EffectiveOrderBy, _isDescending, _groupBy, _isGroupDescending);
        }

        private List<ResolvedEvent> GenerateBatch(LogModel log, int count)
        {
            var batch = new List<ResolvedEvent>(count);

            for (int i = 0; i < count; i++)
            {
                var resolved = GenerateEvent(log);
                batch.Add(resolved);
                log.Events.Add(resolved);
            }

            return batch;
        }

        private ResolvedEvent GenerateEvent(LogModel log)
        {
            log.LastTime = log.LastTime.AddMilliseconds(1 + _rng.Next(1000));

            return new ResolvedEvent(log.Name, LogPathType.Channel)
            {
                RecordId = log.NextRecordId++,
                TimeCreated = log.LastTime,
                Id = 1000 + _rng.Next(8),
                Level = s_levels[_rng.Next(s_levels.Length)],
                Source = s_sources[_rng.Next(s_sources.Length)],
                TaskCategory = s_tasks[_rng.Next(s_tasks.Length)],
                ComputerName = s_computers[_rng.Next(s_computers.Length)],
                Keywords = s_keywords[_rng.Next(s_keywords.Length)],
                UserId = s_sids[_rng.Next(s_sids.Length)],
                ProcessId = _rng.Next(3) == 0 ? null : 100 + _rng.Next(50),
                ThreadId = _rng.Next(3) == 0 ? null : 200 + _rng.Next(50),
                ActivityId = _rng.Next(3) == 0 ? null : NextGuid()
            };
        }

        private IEnumerable<ResolvedEvent> LiveEvents() => _logs.Where(l => l.IsOpen).SelectMany(l => l.Events);

        private Guid NextGuid()
        {
            var bytes = new byte[16];
            _rng.NextBytes(bytes);

            return new Guid(bytes);
        }

        private void PickRandomSort()
        {
            _orderBy = s_orderBys[_rng.Next(s_orderBys.Length)];
            _isDescending = _rng.Next(2) == 0;
            _groupBy = s_groupBys[_rng.Next(s_groupBys.Length)];
            _isGroupDescending = _rng.Next(2) == 0;
        }

        private sealed class LogModel(string name, DateTime startTime)
        {
            public List<ResolvedEvent> Events { get; } = [];

            public bool IsOpen { get; set; } = true;

            public DateTime LastTime { get; set; } = startTime;

            public string Name { get; } = name;

            public long NextRecordId { get; set; } = 1;
        }
    }
}

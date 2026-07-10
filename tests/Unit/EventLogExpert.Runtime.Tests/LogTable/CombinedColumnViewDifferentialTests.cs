// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;

namespace EventLogExpert.Runtime.Tests.LogTable;

/// <summary>
///     Differential parity for <see cref="CombinedColumnView" /> (the live SoA multi-log backing) against the
///     relocated AoS <c>ReferenceCombinedView</c> oracle, locking the column-direct K-way merge for the operations
///     selection and the highlight cache depend on: <c>Count</c>, <c>LocatorAt</c>, <c>Rank</c>, <c>ResolveByKey</c>, and
///     a re-merge after a per-log append.
/// </summary>
public sealed class CombinedColumnViewDifferentialTests
{
    private static readonly string[] s_computers = ["HOST-1", "HOST-2"];
    private static readonly ColumnName?[] s_groupBys = [null, ColumnName.Source, ColumnName.Level];
    private static readonly string[] s_levels = ["Information", "Warning", "Error", "Critical"];
    private static readonly ColumnName?[] s_orderBys =
        [null, ColumnName.DateAndTime, ColumnName.Source, ColumnName.Level, ColumnName.EventId];
    private static readonly string[] s_sources = ["Provider.A", "Provider.B", "Provider.C"];
    private static readonly string[] s_tasks = ["", "Logon", "Service"];

    [Fact]
    public void CombinedColumnView_MatchesReferenceOracle_AfterPerLogReMerge()
    {
        // Design SS14 test 15: appending to one log and re-merging must keep the combined column view aligned with the
        // AoS oracle (both Rank/LocatorAt/ResolveByKey and merged order) at the same and grown corpus.
        var corpus = new Corpus(seed: 31337, logCount: 3);
        corpus.SeedInterleaved(totalEvents: 210);

        var contexts = new[]
        {
            new SortContext(ColumnName.DateAndTime, true, null, false),
            new SortContext(ColumnName.Source, false, ColumnName.Level, false),
        };

        foreach (var context in contexts)
        {
            var (column, oracle) = corpus.BuildViews(context);
            AssertParity(column, oracle, corpus, $"initial orderBy={context.OrderBy}");
        }

        corpus.Append(logIndex: 1, count: 75);

        foreach (var context in contexts)
        {
            var (column, oracle) = corpus.BuildViews(context);
            AssertParity(column, oracle, corpus, $"after-append orderBy={context.OrderBy}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(2026)]
    [InlineData(8675309)]
    public void CombinedColumnView_MatchesReferenceOracle_OverRandomContexts(int seed)
    {
        var corpus = new Corpus(seed, logCount: 2 + (seed % 3));
        corpus.SeedInterleaved(totalEvents: 300);

        foreach (var orderBy in s_orderBys)
        {
            foreach (var groupBy in s_groupBys)
            {
                foreach (bool descending in new[] { true, false })
                {
                    var context = new SortContext(orderBy, descending, groupBy, false);
                    var (column, oracle) = corpus.BuildViews(context);

                    AssertParity(column, oracle, corpus, $"seed={seed} orderBy={orderBy} groupBy={groupBy} desc={descending}");
                }
            }
        }
    }

    private static void AssertParity(CombinedColumnView column, ReferenceCombinedView oracle, Corpus corpus, string label)
    {
        Assert.Equal(oracle.Count, column.Count);

        // Merged order (LocatorAt) and Rank parity at every position: the corpus is totally ordered, so the SoA
        // column-direct merge and the AoS oracle must resolve identically, row for row.
        for (int i = 0; i < oracle.Count; i++)
        {
            var locator = column.LocatorAt(i);
            int columnRank = column.Rank(locator);

            Assert.True(SameEvent(oracle[i], column.GetDetailLean(locator)), $"LocatorAt/order mismatch at {label}[{i}].");
            Assert.Equal(i, columnRank);
            Assert.Equal(oracle.Rank(oracle[i]), columnRank);
        }

        // ResolveByKey parity: the SoA value-key resolve must land on the same event (and rank) as the AoS
        // reference-or-equivalent-key resolve for a spread of in-corpus events.
        foreach (var probe in corpus.SampleEvents())
        {
            var oracleResolved = oracle.ResolveByKey(probe);
            Assert.NotNull(oracleResolved);

            Assert.True(ValueKey.TryCreate(probe, out var key));

            var columnLocator = column.ResolveByKey(key);
            Assert.NotNull(columnLocator);

            Assert.True(
                SameEvent(oracleResolved, column.GetDetailLean(columnLocator.Value)),
                $"ResolveByKey target mismatch at {label}.");
            Assert.Equal(oracle.Rank(oracleResolved), column.Rank(columnLocator.Value));
        }

        // An event from an absent log resolves to nothing in either view.
        var foreign = new ResolvedEvent("AbsentLog", LogPathType.Channel)
        {
            RecordId = long.MaxValue,
            TimeCreated = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Id = 1,
            Source = "Provider.A",
            Level = "Information"
        };

        Assert.Equal(-1, oracle.Rank(foreign));
        Assert.Null(oracle.ResolveByKey(foreign));
        Assert.True(ValueKey.TryCreate(foreign, out var foreignKey));
        Assert.Null(column.ResolveByKey(foreignKey));
        Assert.Equal(-1, column.Rank(new EventLocator(EventLogId.Create(), 0, 0)));
    }

    private static bool SameEvent(ResolvedEvent expected, ResolvedEvent actual) =>
        expected.RecordId == actual.RecordId
        && expected.TimeCreated == actual.TimeCreated
        && expected.Id == actual.Id
        && expected.LogPathType == actual.LogPathType
        && string.Equals(expected.OwningLog, actual.OwningLog, StringComparison.Ordinal)
        && string.Equals(expected.Level, actual.Level, StringComparison.Ordinal)
        && string.Equals(expected.Source, actual.Source, StringComparison.Ordinal)
        && string.Equals(expected.TaskCategory, actual.TaskCategory, StringComparison.Ordinal);

    private sealed class Corpus
    {
        private readonly EventLogId[] _logIds;
        private readonly long[] _nextRecordId;
        private readonly List<List<ResolvedEvent>> _perLog;
        private readonly Random _rng;

        private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Corpus(int seed, int logCount)
        {
            _rng = new Random(seed);
            _perLog = new List<List<ResolvedEvent>>(logCount);
            _logIds = new EventLogId[logCount];
            _nextRecordId = new long[logCount];

            for (int k = 0; k < logCount; k++)
            {
                _perLog.Add([]);
                _logIds[k] = EventLogId.Create();
                _nextRecordId[k] = 1;
            }
        }

        public void Append(int logIndex, int count)
        {
            for (int i = 0; i < count; i++) { AppendOne(logIndex); }
        }

        public (CombinedColumnView Column, ReferenceCombinedView Oracle) BuildViews(SortContext context)
        {
            var columnViews = new List<EventColumnView>(_perLog.Count);

            for (int k = 0; k < _perLog.Count; k++)
            {
                columnViews.Add(DisplayViewTestFactory.Build(_logIds[k], _perLog[k], context));
            }

            return (new CombinedColumnView(columnViews, context), new ReferenceCombinedView(_perLog, context));
        }

        public IEnumerable<ResolvedEvent> SampleEvents()
        {
            foreach (var log in _perLog)
            {
                if (log.Count == 0) { continue; }

                foreach (int index in new[] { 0, log.Count / 4, log.Count / 2, (3 * log.Count) / 4, log.Count - 1 })
                {
                    yield return log[index];
                }
            }
        }

        public void SeedInterleaved(int totalEvents)
        {
            // Guarantee every log is non-empty so every log participates in the merge, then interleave the rest at random
            // so the logs weave together in the merged stream.
            for (int k = 0; k < _perLog.Count; k++) { AppendOne(k); }

            for (int i = _perLog.Count; i < totalEvents; i++) { AppendOne(_rng.Next(_perLog.Count)); }
        }

        private void AppendOne(int logIndex)
        {
            _clock = _clock.AddMilliseconds(1 + _rng.Next(50)); // globally unique + monotonic -> a total order

            _perLog[logIndex].Add(new ResolvedEvent($"Log{logIndex}", LogPathType.Channel)
            {
                RecordId = _nextRecordId[logIndex]++,
                TimeCreated = _clock,
                Id = 1000 + _rng.Next(6),
                Level = s_levels[_rng.Next(s_levels.Length)],
                Source = s_sources[_rng.Next(s_sources.Length)],
                TaskCategory = s_tasks[_rng.Next(s_tasks.Length)],
                ComputerName = s_computers[_rng.Next(s_computers.Length)]
            });
        }
    }

    // Test-only re-implementation of the deleted AoS CombinedEventView oracle over a reference-sorted flattened list. The
    // corpus is totally ordered (distinct OwningLog per log, globally unique monotonic clock, per-log RecordId), so the
    // K-way merge the production oracle performed collapses to sorting the concatenated events by the reference comparer;
    // Rank/ResolveByKey reproduce the oracle's reference-or-value-key window match verbatim.
    private sealed class ReferenceCombinedView
    {
        private readonly Comparison<ResolvedEvent> _comparer;
        private readonly ResolvedEvent[] _merged;

        public ReferenceCombinedView(IReadOnlyList<IReadOnlyList<ResolvedEvent>> perLog, SortContext context)
        {
            var flattened = new List<ResolvedEvent>();

            foreach (var log in perLog) { flattened.AddRange(log); }

            _comparer = AosReferenceOrdering.Reference(
                context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

            int[] order = AosReferenceOrdering.Order(
                flattened, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);
            _merged = new ResolvedEvent[order.Length];

            for (int i = 0; i < order.Length; i++) { _merged[i] = flattened[order[i]]; }
        }

        public int Count => _merged.Length;

        public ResolvedEvent this[int index] => _merged[index];

        public int Rank(ResolvedEvent target)
        {
            ArgumentNullException.ThrowIfNull(target);

            for (int i = LowerBound(target); i < _merged.Length && _comparer(_merged[i], target) == 0; i++)
            {
                if (Matches(_merged[i], target)) { return i; }
            }

            return -1;
        }

        public ResolvedEvent? ResolveByKey(ResolvedEvent candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            for (int i = LowerBound(candidate); i < _merged.Length && _comparer(_merged[i], candidate) == 0; i++)
            {
                if (Matches(_merged[i], candidate)) { return _merged[i]; }
            }

            return null;
        }

        // Reference first, then the (RecordId + time + OwningLog + LogName) value key; a null RecordId never matches by key
        // so distinct error-read events stay separate - identical to the deleted oracle's window scan.
        private static bool Matches(ResolvedEvent current, ResolvedEvent target)
        {
            if (ReferenceEquals(current, target)) { return true; }

            if (current.RecordId is null || target.RecordId is null) { return false; }

            return current.RecordId == target.RecordId
                && current.TimeCreated == target.TimeCreated
                && string.Equals(current.OwningLog, target.OwningLog, StringComparison.Ordinal)
                && string.Equals(current.LogName, target.LogName, StringComparison.Ordinal);
        }

        private int LowerBound(ResolvedEvent target)
        {
            int low = 0;
            int high = _merged.Length;

            while (low < high)
            {
                int mid = low + ((high - low) >> 1);

                if (_comparer(_merged[mid], target) < 0) { low = mid + 1; }
                else { high = mid; }
            }

            return low;
        }
    }
}

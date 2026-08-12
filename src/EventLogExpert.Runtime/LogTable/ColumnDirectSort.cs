// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

internal static class ColumnDirectSort
{
    internal static int[] SortColumnDirect(
        IEventColumnReader reader,
        ReadOnlySpan<int> survivors,
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int[] result = [.. survivors];

        if (result.Length < 2) { return result; }

        var keys = ColumnDirectKeys.Materialize(reader, orderBy, groupBy, cancellationToken);
        Comparison<int> comparison = keys.BuildComparison(orderBy, isDescending, groupBy, isGroupDescending);
        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(result, comparison);
        cancellationToken.ThrowIfCancellationRequested();

        return result;
    }

    private sealed class ColumnDirectKeys
    {
        private static readonly int s_columnCount = Enum.GetValues<ColumnName>().Length;

        private readonly bool[]?[] _guidHas;
        private readonly Guid[]?[] _guidValues;
        private readonly bool[]?[] _numericHas;
        private readonly long[]?[] _numericValues;
        private readonly int[] _owningLogRank;
        private readonly List<int> _pooledColumns;
        private readonly int[]?[] _stringRank;

        private int _nullRank;
        private int[] _rankByPoolIndex;

        private ColumnDirectKeys(int count)
        {
            Count = count;
            _numericValues = new long[s_columnCount][];
            _numericHas = new bool[s_columnCount][];
            _guidValues = new Guid[s_columnCount][];
            _guidHas = new bool[s_columnCount][];
            _stringRank = new int[s_columnCount][];
            _owningLogRank = new int[count];
            _pooledColumns = [];
            _rankByPoolIndex = [];
        }

        private int Count { get; }

        internal static ColumnDirectKeys Materialize(
            IEventColumnReader reader, ColumnName? orderBy, ColumnName? groupBy, CancellationToken cancellationToken)
        {
            var keys = new ColumnDirectKeys(reader.Count);

            keys.MaterializeColumn(reader, ColumnName.RecordId, cancellationToken);
            keys.MaterializeOwningLog(reader, cancellationToken);

            if (groupBy is not null || orderBy is null) { keys.MaterializeColumn(reader, ColumnName.DateAndTime, cancellationToken); }

            if (orderBy is { } orderColumn) { keys.MaterializeColumn(reader, orderColumn, cancellationToken); }

            if (groupBy is { } groupColumn) { keys.MaterializeColumn(reader, groupColumn, cancellationToken); }

            keys.RankPooledColumns(reader, cancellationToken);

            return keys;
        }

        internal Comparison<int> BuildComparison(
            ColumnName? orderBy,
            bool isDescending,
            ColumnName? groupBy,
            bool isGroupDescending)
        {
            if (groupBy is { } groupColumn)
            {
                ColumnName withinColumn = orderBy ?? ColumnName.DateAndTime;

                return (a, b) => WithIndexTieBreak(
                    GroupedChain(a, b, groupColumn, withinColumn, isGroupDescending, isDescending), a, b);
            }

            if (orderBy is null)
            {
                return isDescending ?
                    (a, b) => WithIndexTieBreak(DefaultChain(b, a), a, b) :
                    (a, b) => WithIndexTieBreak(DefaultChain(a, b), a, b);
            }

            ColumnName orderColumn = orderBy.Value;

            return isDescending ?
                (a, b) => WithIndexTieBreak(OrderedChain(b, a, orderColumn), a, b) :
                (a, b) => WithIndexTieBreak(OrderedChain(a, b, orderColumn), a, b);
        }

        private static void CollectUsedPoolIndices(int[] rawPoolIndices, bool[] seen, List<int> used, CancellationToken cancellationToken)
        {
            int scanned = 0;

            foreach (int poolIndex in rawPoolIndices)
            {
                if ((scanned++ & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                if (poolIndex >= 0 && !seen[poolIndex])
                {
                    seen[poolIndex] = true;
                    used.Add(poolIndex);
                }
            }
        }

        private static int CompareRank(int[] rank, int a, int b) => rank[a].CompareTo(rank[b]);

        private static int[] DenseRank(string[] values, int[] rankByPosition, CancellationToken cancellationToken)
        {
            int length = values.Length;
            var order = new int[length];

            for (int index = 0; index < length; index++)
            {
                if ((index & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                order[index] = index;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Array.Sort(order, (x, y) => string.Compare(values[x], values[y], StringComparison.Ordinal));
            cancellationToken.ThrowIfCancellationRequested();

            int rank = 0;
            rankByPosition[order[0]] = 0;

            for (int position = 1; position < length; position++)
            {
                if ((position & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                if (!string.Equals(values[order[position]], values[order[position - 1]], StringComparison.Ordinal))
                {
                    rank++;
                }

                rankByPosition[order[position]] = rank;
            }

            return order;
        }

        private static int WithIndexTieBreak(int chain, int a, int b) => chain != 0 ? chain : a.CompareTo(b);

        private int CompareColumn(ColumnName column, int a, int b) => column switch
        {
            ColumnName.RecordId or ColumnName.ProcessId or ColumnName.ThreadId or ColumnName.EventId
                or ColumnName.DateAndTime => CompareNumeric(column, a, b),
            ColumnName.ActivityId => CompareGuid(column, a, b),
            _ => CompareRank(_stringRank[(int)column]!, a, b)
        };

        private int CompareGuid(ColumnName column, int a, int b)
        {
            bool[] has = _guidHas[(int)column]!;

            if (!has[a] || !has[b]) { return has[a] == has[b] ? 0 : (has[a] ? 1 : -1); }

            Guid[] values = _guidValues[(int)column]!;

            return values[a].CompareTo(values[b]);
        }

        private int CompareNumeric(ColumnName column, int a, int b)
        {
            bool[] has = _numericHas[(int)column]!;

            if (!has[a] || !has[b]) { return has[a] == has[b] ? 0 : (has[a] ? 1 : -1); }

            long[] values = _numericValues[(int)column]!;

            return values[a].CompareTo(values[b]);
        }

        private int CompareOwningLog(int a, int b) => _owningLogRank[a].CompareTo(_owningLogRank[b]);

        private int DefaultChain(int a, int b)
        {
            int byRecordId = CompareColumn(ColumnName.RecordId, a, b);

            if (byRecordId != 0) { return byRecordId; }

            int byTime = CompareColumn(ColumnName.DateAndTime, a, b);

            return byTime != 0 ? byTime : CompareOwningLog(a, b);
        }

        private int FallbackTieBreak(int recordIdResult, int a, int b) =>
            recordIdResult != 0 ? recordIdResult : CompareOwningLog(a, b);

        private int GroupedChain(
            int a,
            int b,
            ColumnName groupColumn,
            ColumnName withinColumn,
            bool isGroupDescending,
            bool isDescending)
        {
            int group = CompareColumn(groupColumn, a, b);

            if (group != 0) { return isGroupDescending ? -Math.Sign(group) : group; }

            int within = CompareColumn(withinColumn, a, b);

            if (within == 0 && withinColumn != ColumnName.DateAndTime)
            {
                within = CompareColumn(ColumnName.DateAndTime, a, b);
            }

            if (within == 0) { within = FallbackTieBreak(CompareColumn(ColumnName.RecordId, a, b), a, b); }

            return isDescending ? -Math.Sign(within) : within;
        }

        private void MaterializeColumn(IEventColumnReader reader, ColumnName column, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (column)
            {
                case ColumnName.RecordId:
                case ColumnName.ProcessId:
                case ColumnName.ThreadId:
                case ColumnName.EventId:
                case ColumnName.DateAndTime:
                    MaterializeNumeric(reader, column);
                    break;
                case ColumnName.ActivityId:
                    MaterializeGuid(reader, column);
                    break;
                case ColumnName.Keywords:
                    MaterializeKeywords(reader, cancellationToken);
                    break;
                default:
                    MaterializePooled(reader, column);
                    break;
            }
        }

        private void MaterializeGuid(IEventColumnReader reader, ColumnName column)
        {
            if (_guidValues[(int)column] is not null) { return; }

            var values = new Guid[Count];
            var has = new bool[Count];
            reader.CopyGuidColumn(ColumnFieldMap.ToFieldId(column), values, has);
            _guidValues[(int)column] = values;
            _guidHas[(int)column] = has;
        }

        private void MaterializeKeywords(IEventColumnReader reader, CancellationToken cancellationToken)
        {
            if (_stringRank[(int)ColumnName.Keywords] is not null) { return; }

            var values = new string[Count];

            for (int index = 0; index < Count; index++)
            {
                if ((index & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                values[index] = reader.GetField(reader.LocatorAt(index), EventFieldId.KeywordsDisplay).AsString();
            }

            var rankByRow = new int[Count];
            DenseRank(values, rankByRow, cancellationToken);
            _stringRank[(int)ColumnName.Keywords] = rankByRow;
        }

        private void MaterializeNumeric(IEventColumnReader reader, ColumnName column)
        {
            if (_numericValues[(int)column] is not null) { return; }

            var values = new long[Count];
            var has = new bool[Count];
            reader.CopyInt64Column(ColumnFieldMap.ToFieldId(column), values, has);
            _numericValues[(int)column] = values;
            _numericHas[(int)column] = has;
        }

        private void MaterializeOwningLog(IEventColumnReader reader, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reader.CopyPoolIndexColumn(EventFieldId.OwningLog, _owningLogRank);
        }

        private void MaterializePooled(IEventColumnReader reader, ColumnName column)
        {
            if (_stringRank[(int)column] is not null) { return; }

            var poolIndices = new int[Count];
            reader.CopyPoolIndexColumn(ColumnFieldMap.ToFieldId(column), poolIndices);

            _stringRank[(int)column] = poolIndices;
            _pooledColumns.Add((int)column);
        }

        private int OrderedChain(int a, int b, ColumnName orderColumn) =>
            WithTieBreak(CompareColumn(orderColumn, a, b), a, b);

        private void RankFromPoolIndices(int[] poolIndices, int[] rankByRow, CancellationToken cancellationToken)
        {
            for (int index = 0; index < poolIndices.Length; index++)
            {
                if ((index & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                int poolIndex = poolIndices[index];
                rankByRow[index] = poolIndex < 0 ? _nullRank : _rankByPoolIndex[poolIndex];
            }
        }

        private void RankPooledColumns(IEventColumnReader reader, CancellationToken cancellationToken)
        {
            IReadOnlyList<string?> pool = reader.Pool;
            int poolCount = pool.Count;
            _rankByPoolIndex = poolCount == 0 ? [] : new int[poolCount];

            var seen = new bool[poolCount];
            var used = new List<int>();
            CollectUsedPoolIndices(_owningLogRank, seen, used, cancellationToken);

            foreach (int columnIndex in _pooledColumns) { CollectUsedPoolIndices(_stringRank[columnIndex]!, seen, used, cancellationToken); }

            if (used.Count == 0)
            {
                _nullRank = -1;
            }
            else
            {
                var usedStrings = new string[used.Count];

                for (int index = 0; index < used.Count; index++)
                {
                    if ((index & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                    usedStrings[index] = pool[used[index]] ?? string.Empty;
                }

                var rankByPosition = new int[used.Count];
                int[] order = DenseRank(usedStrings, rankByPosition, cancellationToken);

                for (int index = 0; index < used.Count; index++)
                {
                    if ((index & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                    _rankByPoolIndex[used[index]] = rankByPosition[index];
                }

                _nullRank = usedStrings[order[0]].Length == 0 ? 0 : -1;
            }

            RankFromPoolIndices(_owningLogRank, _owningLogRank, cancellationToken);

            foreach (int columnIndex in _pooledColumns)
            {
                int[] columnRanks = _stringRank[columnIndex]!;
                RankFromPoolIndices(columnRanks, columnRanks, cancellationToken);
            }
        }

        private int WithTieBreak(int primary, int a, int b) =>
            primary != 0 ? primary : FallbackTieBreak(CompareColumn(ColumnName.RecordId, a, b), a, b);
    }
}

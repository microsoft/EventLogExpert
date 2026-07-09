// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Structured;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     A call-local, mutable builder for the global EventData schema table: deduped arrays of field-name pool
///     indices. <see cref="Intern" /> returns a stable id per distinct field-name-index sequence; <see cref="ToTable" />
///     returns a new immutable table structurally extending the base. Discarded when the build returns.
/// </summary>
internal sealed class EventDataSchemaBuilder
{
    private readonly Dictionary<int[], int> _index;
    private readonly List<int[]> _schemas;

    internal EventDataSchemaBuilder(ImmutableArray<int[]> baseSchemas)
    {
        _schemas = baseSchemas.IsDefault ? [] : [.. baseSchemas];
        _index = new Dictionary<int[], int>(FieldNameIndicesComparer.Instance);

        for (int id = 0; id < _schemas.Count; id++) { _index[_schemas[id]] = id; }
    }

    internal int Intern(int[] fieldNameIndices)
    {
        if (_index.TryGetValue(fieldNameIndices, out int existing)) { return existing; }

        int id = _schemas.Count;
        _schemas.Add(fieldNameIndices);
        _index[fieldNameIndices] = id;

        return id;
    }

    internal ImmutableArray<int[]> ToTable() => [.. _schemas];

    private sealed class FieldNameIndicesComparer : IEqualityComparer<int[]>
    {
        internal static readonly FieldNameIndicesComparer Instance = new();

        public bool Equals(int[]? x, int[]? y)
        {
            if (ReferenceEquals(x, y)) { return true; }

            if (x is null || y is null) { return false; }

            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(int[] obj)
        {
            HashCode hash = new();
            hash.AddBytes(MemoryMarshal.AsBytes(obj.AsSpan()));

            return hash.ToHashCode();
        }
    }
}

/// <summary>
///     An immutable, chunked, columnar per-log snapshot of resolved events. Older rows are columnarized into a list
///     of self-contained <see cref="EventColumnChunk" />s addressed by a lazy row prefix-sum; the newest rows stay as a
///     bounded array-of-structs <see cref="ResolvedEvent" /> tail. <see cref="Build" /> and <see cref="Append" /> are pure
///     functions returning a new snapshot that reference-shares prior chunks, the interned pool, and the schema table, so
///     a published snapshot is safe to read while a later ingest builds the next one.
/// </summary>
public sealed class EventColumnStore
{
    // Target sealed-chunk size. The pending tail columnarizes into one new chunk each time it reaches this many rows,
    // giving amortized O(1) append with no partially filled columnar chunks.
    private const int TargetChunkSize = 4096;

    private readonly ImmutableList<ResolvedEvent> _pendingTail;
    private readonly EventColumnPool _pool;
    private readonly ImmutableArray<int[]> _schemas;
    private readonly ImmutableList<EventColumnChunk> _sealedChunks;
    private readonly int _sealedCount;

    // Prefix-sum over sealed chunk row counts (_sealedPrefix[i] = rows before chunk i; length _sealedChunks.Count + 1),
    // built lazily on first sealed-row access. Volatile-published; the instance is immutable so a recompute is benign.
    private int[]? _sealedPrefix;

    private EventColumnStore(
        ImmutableList<EventColumnChunk> sealedChunks,
        ImmutableList<ResolvedEvent> pendingTail,
        EventColumnPool pool,
        ImmutableArray<int[]> schemas,
        int sealedCount,
        int generation,
        long contentVersion,
        long minTimeTicks,
        long maxTimeTicks)
    {
        _sealedChunks = sealedChunks;
        _pendingTail = pendingTail;
        _pool = pool;
        _schemas = schemas;
        _sealedCount = sealedCount;
        Generation = generation;
        ContentVersion = contentVersion;
        MinTimeTicks = minTimeTicks;
        MaxTimeTicks = maxTimeTicks;
    }

    /// <summary>An empty snapshot (generation 0, no rows) that seeds a newly opened log before its first ingest.</summary>
    public static EventColumnStore Empty { get; } = Build([], generation: 0, contentVersion: 0);

    /// <summary>Log-lifetime-monotonic ingest counter, bumped on every append and never reset across generations.</summary>
    public long ContentVersion { get; }

    /// <summary>The total row count: the sealed columnar rows plus the pending array-of-structs tail.</summary>
    public int Count => _sealedCount + _pendingTail.Count;

    /// <summary>Per-log reload counter, stable across appends and bumped only by a new-generation build.</summary>
    public int Generation { get; }

    /// <summary>
    ///     The largest <see cref="ResolvedEvent.TimeCreated" /> UTC-tick value across every row, maintained O(1) at build
    ///     and append. An empty store (<see cref="Count" /> 0) reports <c>long.MinValue</c> (no range); read the span through
    ///     <see cref="TryGetTimeRange" />.
    /// </summary>
    internal long MaxTimeTicks { get; }

    /// <summary>
    ///     The smallest <see cref="ResolvedEvent.TimeCreated" /> UTC-tick value across every row, maintained O(1) at
    ///     build and append. An empty store (<see cref="Count" /> 0) reports <c>long.MaxValue</c> (no range); read the span
    ///     through <see cref="TryGetTimeRange" />.
    /// </summary>
    internal long MinTimeTicks { get; }

    internal int PoolDistinctCount => _pool.DistinctCount;

    internal int SchemaCount => _schemas.Length;

    internal int SealedChunkCount => _sealedChunks.Count;

    internal int SealedCount => _sealedCount;

    /// <summary>
    ///     Columnarizes an initial batch into ceil(n / <see cref="TargetChunkSize" />) sealed chunks (empty pending
    ///     tail), stamping the snapshot with <paramref name="generation" /> and <paramref name="contentVersion" />.
    /// </summary>
    public static EventColumnStore Build(IReadOnlyList<ResolvedEvent> batch, int generation, long contentVersion)
    {
        ArgumentNullException.ThrowIfNull(batch);

        ImmutableList<EventColumnChunk> chunks = ImmutableList<EventColumnChunk>.Empty;
        EventColumnPool pool = EventColumnPool.Empty;
        ImmutableArray<int[]> schemas = ImmutableArray<int[]>.Empty;

        for (int start = 0; start < batch.Count; start += TargetChunkSize)
        {
            int count = Math.Min(TargetChunkSize, batch.Count - start);
            (EventColumnChunk chunk, pool, schemas) = BuildChunk(batch, start, count, pool, schemas);
            chunks = chunks.Add(chunk);
        }

        (long minTimeTicks, long maxTimeTicks) = ComputeTimeRange(batch);

        return new EventColumnStore(
            chunks, ImmutableList<ResolvedEvent>.Empty, pool, schemas, batch.Count, generation, contentVersion, minTimeTicks, maxTimeTicks);
    }

    /// <summary>
    ///     Returns a new snapshot with <paramref name="batch" /> appended to the pending tail; whenever the tail reaches
    ///     <see cref="TargetChunkSize" /> rows it seals the first N into one new columnar chunk (interning into the pool and
    ///     schema table). Bumps <see cref="ContentVersion" />, leaves <see cref="Generation" /> unchanged, and preserves every
    ///     prior global index. An empty batch is a no-op that returns the same instance.
    /// </summary>
    public EventColumnStore Append(IReadOnlyList<ResolvedEvent> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0) { return this; }

        ImmutableList<ResolvedEvent> pending = _pendingTail.AddRange(batch);
        ImmutableList<EventColumnChunk> chunks = _sealedChunks;
        EventColumnPool pool = _pool;
        ImmutableArray<int[]> schemas = _schemas;
        int sealedCount = _sealedCount;

        while (pending.Count >= TargetChunkSize)
        {
            (EventColumnChunk chunk, pool, schemas) = BuildChunk(pending, 0, TargetChunkSize, pool, schemas);
            chunks = chunks.Add(chunk);
            pending = pending.RemoveRange(0, TargetChunkSize);
            sealedCount += TargetChunkSize;
        }

        // Non-empty batch (the empty case returned above), so combine its range with the prior snapshot's to widen O(1).
        (long batchMinTimeTicks, long batchMaxTimeTicks) = ComputeTimeRange(batch);
        long minTimeTicks = Math.Min(MinTimeTicks, batchMinTimeTicks);
        long maxTimeTicks = Math.Max(MaxTimeTicks, batchMaxTimeTicks);

        return new EventColumnStore(
            chunks, pending, pool, schemas, sealedCount, Generation, ContentVersion + 1, minTimeTicks, maxTimeTicks);
    }

    /// <summary>
    ///     Creates a read-only <see cref="IEventColumnReader" /> over this snapshot, stamping every
    ///     <see cref="EventLocator" /> it mints with <paramref name="logId" />. The concrete reader type stays internal, so
    ///     callers depend only on the public reader contract.
    /// </summary>
    /// <param name="logId">The log identity the returned reader records on the locators it produces.</param>
    /// <returns>A reader bound to this immutable snapshot and <paramref name="logId" />.</returns>
    public IEventColumnReader CreateReader(EventLogId logId) => new EventColumnStoreReader(logId, this);

    /// <summary>
    ///     The <see cref="ResolvedEvent.TimeCreated" /> UTC-tick span across every row: <c>true</c> with [
    ///     <paramref name="minTicks" />, <paramref name="maxTicks" />] set, or <c>false</c> when the store holds no rows (no
    ///     range). The ticks share the time column's basis, so the span matches it exactly.
    /// </summary>
    public bool TryGetTimeRange(out long minTicks, out long maxTicks)
    {
        minTicks = MinTimeTicks;
        maxTicks = MaxTimeTicks;

        return Count > 0;
    }

    /// <summary>
    ///     Bulk-copies the <see cref="ResolvedEvent.ActivityId" /> column into caller-owned flat arrays over the whole
    ///     physical range [0, <see cref="Count" />): sealed rows are copied a chunk at a time (no per-row search), the bounded
    ///     pending tail row by row. <paramref name="hasValue" /> is false for an absent (null) ActivityId.
    /// </summary>
    internal void CopyActivityId(Guid[] values, bool[] hasValue)
    {
        int offset = 0;

        foreach (EventColumnChunk chunk in _sealedChunks)
        {
            chunk.ActivityIdColumn.CopyTo(values.AsSpan(offset));
            chunk.ActivityIdHasColumn.CopyTo(hasValue.AsSpan(offset));
            offset += chunk.RowCount;
        }

        for (int index = _sealedCount; index < Count; index++)
        {
            Guid? value = Pending(index).ActivityId;
            values[index] = value ?? Guid.Empty;
            hasValue[index] = value.HasValue;
        }
    }

    /// <summary>Bulk-copies the always-present <see cref="ResolvedEvent.Id" /> column, widened to <see cref="long" />.</summary>
    internal void CopyId(long[] values, bool[] hasValue)
    {
        int offset = 0;

        foreach (EventColumnChunk chunk in _sealedChunks)
        {
            ReadOnlySpan<int> column = chunk.IdColumn;

            for (int row = 0; row < column.Length; row++)
            {
                values[offset + row] = column[row];
                hasValue[offset + row] = true;
            }

            offset += chunk.RowCount;
        }

        for (int index = _sealedCount; index < Count; index++)
        {
            values[index] = Pending(index).Id;
            hasValue[index] = true;
        }
    }

    /// <summary>Bulk-copies the nullable <see cref="ResolvedEvent.ProcessId" /> column, widened to <see cref="long" />.</summary>
    internal void CopyProcessId(long[] values, bool[] hasValue)
    {
        int offset = 0;

        foreach (EventColumnChunk chunk in _sealedChunks)
        {
            ReadOnlySpan<int> column = chunk.ProcessIdColumn;
            chunk.ProcessIdHasColumn.CopyTo(hasValue.AsSpan(offset));

            for (int row = 0; row < column.Length; row++) { values[offset + row] = column[row]; }

            offset += chunk.RowCount;
        }

        for (int index = _sealedCount; index < Count; index++)
        {
            int? value = Pending(index).ProcessId;
            values[index] = value ?? 0;
            hasValue[index] = value.HasValue;
        }
    }

    /// <summary>Bulk-copies the nullable <see cref="ResolvedEvent.RecordId" /> column.</summary>
    internal void CopyRecordId(long[] values, bool[] hasValue)
    {
        int offset = 0;

        foreach (EventColumnChunk chunk in _sealedChunks)
        {
            chunk.RecordIdColumn.CopyTo(values.AsSpan(offset));
            chunk.RecordIdHasColumn.CopyTo(hasValue.AsSpan(offset));
            offset += chunk.RowCount;
        }

        for (int index = _sealedCount; index < Count; index++)
        {
            long? value = Pending(index).RecordId;
            values[index] = value ?? 0;
            hasValue[index] = value.HasValue;
        }
    }

    /// <summary>Bulk-copies the sealed rows' pool-index column for <paramref name="column" /> (pending rows unset).</summary>
    internal void CopySealedPoolIndex(EventColumnField column, int[] poolIndices)
    {
        int offset = 0;

        foreach (EventColumnChunk chunk in _sealedChunks)
        {
            chunk.PoolIndexColumn(column).CopyTo(poolIndices.AsSpan(offset));
            offset += chunk.RowCount;
        }
    }

    /// <summary>Bulk-copies the nullable <see cref="ResolvedEvent.ThreadId" /> column, widened to <see cref="long" />.</summary>
    internal void CopyThreadId(long[] values, bool[] hasValue)
    {
        int offset = 0;

        foreach (EventColumnChunk chunk in _sealedChunks)
        {
            ReadOnlySpan<int> column = chunk.ThreadIdColumn;
            chunk.ThreadIdHasColumn.CopyTo(hasValue.AsSpan(offset));

            for (int row = 0; row < column.Length; row++) { values[offset + row] = column[row]; }

            offset += chunk.RowCount;
        }

        for (int index = _sealedCount; index < Count; index++)
        {
            int? value = Pending(index).ThreadId;
            values[index] = value ?? 0;
            hasValue[index] = value.HasValue;
        }
    }

    /// <summary>Bulk-copies the always-present <see cref="ResolvedEvent.TimeCreated" /> column as raw UTC ticks.</summary>
    internal void CopyTimeTicks(long[] values, bool[] hasValue)
    {
        int offset = 0;

        foreach (EventColumnChunk chunk in _sealedChunks)
        {
            ReadOnlySpan<long> column = chunk.TimeTicksColumn;
            column.CopyTo(values.AsSpan(offset));
            hasValue.AsSpan(offset, column.Length).Fill(true);
            offset += chunk.RowCount;
        }

        for (int index = _sealedCount; index < Count; index++)
        {
            values[index] = Pending(index).TimeCreated.Ticks;
            hasValue[index] = true;
        }
    }

    /// <summary>
    ///     Reconstructs a byte-faithful <see cref="ResolvedEvent" /> for the row at <paramref name="index" />,
    ///     reproducing every field's observable value. A pending row is returned as-is (perfect fidelity, zero work); a sealed
    ///     row is rebuilt from its columnar representation, re-hydrating pooled strings and unpacking each &lt;EventData&gt;
    ///     field.
    /// </summary>
    internal ResolvedEvent GetDetail(int index)
    {
        if (IsPending(index)) { return GetPendingEvent(index); }

        return ReconstructLeanEvent(index) with
        {
            Xml = PoolGet(RawPoolIndex(EventColumnField.Xml, index)) ?? string.Empty,
            UserData = ReconstructUserData(index),
            EventDataValues = ReconstructEventDataValues(index),
            EventDataSchema = ReconstructSchema(index)
        };
    }

    /// <summary>
    ///     The viewport variant of <see cref="GetDetail" />: reconstructs the grid fields (all scalars incl.
    ///     <see cref="ResolvedEvent.Description" />, <see cref="ResolvedEvent.Keywords" />, and
    ///     <see cref="ResolvedEvent.UserId" />) but leaves the detail-only <see cref="ResolvedEvent.UserData" />,
    ///     <see cref="ResolvedEvent.Xml" />, and &lt;EventData&gt; empty. A pending row is returned as-is with its detail
    ///     fields intact (best-effort); a sealed reconstruction leaves them empty until <see cref="GetDetail" /> materializes
    ///     them.
    /// </summary>
    internal ResolvedEvent GetDetailLean(int index) =>
        IsPending(index) ? GetPendingEvent(index) : ReconstructLeanEvent(index);

    /// <summary>
    ///     The pending-tail <see cref="ResolvedEvent" /> at <paramref name="index" />. Only sealed rows have a columnar
    ///     representation, so a consumer reads a pending row's complex data (keywords, UserData, EventData, pooled fields)
    ///     straight off this event. Throws when <paramref name="index" /> is a sealed row (check <see cref="IsPending" />
    ///     first).
    /// </summary>
    internal ResolvedEvent GetPendingEvent(int index) =>
        index < _sealedCount ?
        throw new ArgumentOutOfRangeException(nameof(index),
            "The row is sealed into a columnar chunk; read it via the column accessors.") :
        Pending(index);

    /// <summary><c>true</c> when the row at <paramref name="index" /> is still in the array-of-structs pending tail.</summary>
    internal bool IsPending(int index) => index >= _sealedCount;

    internal string? PoolGet(int poolIndex) => _pool.Get(poolIndex);

    internal Guid RawActivityId(int index, out bool hasValue)
    {
        if (index < _sealedCount)
        {
            (EventColumnChunk chunk, int row) = Locate(index);

            return chunk.RowActivityId(row, out hasValue);
        }

        Guid? value = Pending(index).ActivityId;
        hasValue = value.HasValue;

        return value ?? Guid.Empty;
    }

    internal int RawEventDataCount(int index)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowEventDataCount(row);
    }

    internal RawEventDataField RawEventDataField(int index, int field)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowEventDataField(row, field);
    }

    internal int RawEventDataSchemaId(int index)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowEventDataSchemaId(row);
    }

    internal int RawId(int index)
    {
        if (index >= _sealedCount)
        {
            return Pending(index).Id;
        }

        (EventColumnChunk chunk, int row) = Locate(index);

        return chunk.RowId(row);

    }

    internal int RawKeywordCount(int index)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowKeywordCount(row);
    }

    internal ReadOnlySpan<int> RawKeywords(int index)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowKeywords(row);
    }

    internal byte RawLogPathType(int index)
    {
        if (index >= _sealedCount)
        {
            return (byte)Pending(index).LogPathType;
        }

        (EventColumnChunk chunk, int row) = Locate(index);

        return chunk.RowLogPathType(row);

    }

    internal int RawPoolIndex(EventColumnField column, int index)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowPoolIndex(column, row);
    }

    internal int RawProcessId(int index, out bool hasValue)
    {
        if (index < _sealedCount)
        {
            (EventColumnChunk chunk, int row) = Locate(index);

            return chunk.RowProcessId(row, out hasValue);
        }

        int? value = Pending(index).ProcessId;
        hasValue = value.HasValue;

        return value ?? 0;
    }

    internal long RawRecordId(int index, out bool hasValue)
    {
        if (index < _sealedCount)
        {
            (EventColumnChunk chunk, int row) = Locate(index);

            return chunk.RowRecordId(row, out hasValue);
        }

        long? value = Pending(index).RecordId;
        hasValue = value.HasValue;

        return value ?? 0;
    }

    internal int RawThreadId(int index, out bool hasValue)
    {
        if (index < _sealedCount)
        {
            (EventColumnChunk chunk, int row) = Locate(index);

            return chunk.RowThreadId(row, out hasValue);
        }

        int? value = Pending(index).ThreadId;
        hasValue = value.HasValue;

        return value ?? 0;
    }

    internal long RawTimeTicks(int index)
    {
        if (index >= _sealedCount)
        {
            return Pending(index).TimeCreated.Ticks;
        }

        (EventColumnChunk chunk, int row) = Locate(index);

        return chunk.RowTimeTicks(row);

    }

    internal int RawUserDataCount(int index)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowUserDataCount(row);
    }

    internal bool RawUserDataIncomplete(int index)
    {
        if (index >= _sealedCount)
        {
            return Pending(index).UserDataIncomplete;
        }

        (EventColumnChunk chunk, int row) = Locate(index);

        return chunk.RowUserDataIncomplete(row);

    }

    internal int RawUserDataPathIndex(int index, int field)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowUserDataPathIndex(row, field);
    }

    internal bool RawUserDataTruncated(int index, int field)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowUserDataTruncated(row, field);
    }

    internal ReadOnlySpan<int> RawUserDataValues(int index, int field)
    {
        (EventColumnChunk chunk, int row) = LocateSealed(index);

        return chunk.RowUserDataValues(row, field);
    }

    internal EventProperty ReconstructEventProperty(RawEventDataField field)
    {
        switch (field.Kind)
        {
            case StoredFieldKind.SByte:
            case StoredFieldKind.Byte:
            case StoredFieldKind.Int16:
            case StoredFieldKind.UInt16:
            case StoredFieldKind.Int32:
            case StoredFieldKind.UInt32:
            case StoredFieldKind.Int64:
            case StoredFieldKind.UInt64:
            case StoredFieldKind.Single:
            case StoredFieldKind.Double:
            case StoredFieldKind.Boolean:
            case StoredFieldKind.DateTime:
            case StoredFieldKind.SizeT:
                return EventProperty.FromPacked(MapBackPackedKind(field.Kind), field.Bits);
            case StoredFieldKind.String:
            case StoredFieldKind.StringForm:
                return EventProperty.FromReference(PoolGet(field.RefIndex));
            case StoredFieldKind.Sid:
                return EventProperty.FromReference(new SecurityIdentifier(PoolGet(field.RefIndex)!));
            case StoredFieldKind.Guid:
                return EventProperty.FromReference(new Guid(field.Bytes));
            case StoredFieldKind.Bytes:
                return EventProperty.FromReference(field.Bytes.ToArray());
            case StoredFieldKind.UInt16Array:
                return EventProperty.FromReference(MemoryMarshal.Cast<byte, ushort>(field.Bytes).ToArray());
            case StoredFieldKind.UInt32Array:
                return EventProperty.FromReference(MemoryMarshal.Cast<byte, uint>(field.Bytes).ToArray());
            case StoredFieldKind.Int32Array:
                return EventProperty.FromReference(MemoryMarshal.Cast<byte, int>(field.Bytes).ToArray());
            case StoredFieldKind.StringArray:
                return EventProperty.FromReference(ReconstructStringArray(field.ValueIndices));
            case StoredFieldKind.Null:
                return EventProperty.FromReference(null);
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field.Kind, "Unknown stored field kind.");
        }
    }

    internal string[] ReconstructStringArray(ReadOnlySpan<int> valueIndices)
    {
        if (valueIndices.Length == 0) { return []; }

        string[] values = new string[valueIndices.Length];

        for (int i = 0; i < valueIndices.Length; i++) { values[i] = PoolGet(valueIndices[i])!; }

        return values;
    }

    /// <summary>The field-name pool indices backing the deduped schema at <paramref name="schemaId" />.</summary>
    internal ReadOnlySpan<int> SchemaFieldNameIndices(int schemaId) => _schemas[schemaId];

    /// <summary>
    ///     Builds a fresh snapshot for a reload: bumps <see cref="Generation" /> and continues (never resets)
    ///     <see cref="ContentVersion" />.
    /// </summary>
    internal EventColumnStore WithReloadGeneration(IReadOnlyList<ResolvedEvent> batch) =>
        Build(batch, Generation + 1, ContentVersion + 1);

    private static (EventColumnChunk Chunk, EventColumnPool Pool, ImmutableArray<int[]> Schemas) BuildChunk(
        IReadOnlyList<ResolvedEvent> events,
        int start,
        int count,
        EventColumnPool pool,
        ImmutableArray<int[]> schemas)
    {
        EventColumnPool.Builder poolBuilder = pool.CreateBuilder();
        EventDataSchemaBuilder schemaBuilder = new(schemas);
        EventColumnChunk chunk = EventColumnChunk.Build(events, start, count, poolBuilder, schemaBuilder);

        return (chunk, poolBuilder.ToPool(), schemaBuilder.ToTable());
    }

    // Min/max of the batch's TimeCreated ticks (the same UTC basis the time column stores). An empty batch yields the
    // (long.MaxValue, long.MinValue) sentinel so a row-less store reports "no range" and an append widens correctly.
    private static (long Min, long Max) ComputeTimeRange(IReadOnlyList<ResolvedEvent> batch)
    {
        long min = long.MaxValue;
        long max = long.MinValue;

        for (int index = 0; index < batch.Count; index++)
        {
            long ticks = batch[index].TimeCreated.Ticks;

            if (ticks < min) { min = ticks; }

            if (ticks > max) { max = ticks; }
        }

        return (min, max);
    }

    private static int FindChunk(int[] prefix, int index)
    {
        int low = 0;
        int high = prefix.Length - 2;

        while (low < high)
        {
            int mid = (low + high + 1) >> 1;

            if (prefix[mid] <= index) { low = mid; }
            else { high = mid - 1; }
        }

        return low;
    }

    private static EventPropertyKind MapBackPackedKind(StoredFieldKind kind) => kind switch
    {
        StoredFieldKind.SByte => EventPropertyKind.SByte,
        StoredFieldKind.Byte => EventPropertyKind.Byte,
        StoredFieldKind.Int16 => EventPropertyKind.Int16,
        StoredFieldKind.UInt16 => EventPropertyKind.UInt16,
        StoredFieldKind.Int32 => EventPropertyKind.Int32,
        StoredFieldKind.UInt32 => EventPropertyKind.UInt32,
        StoredFieldKind.Int64 => EventPropertyKind.Int64,
        StoredFieldKind.UInt64 => EventPropertyKind.UInt64,
        StoredFieldKind.Single => EventPropertyKind.Single,
        StoredFieldKind.Double => EventPropertyKind.Double,
        StoredFieldKind.Boolean => EventPropertyKind.Boolean,
        StoredFieldKind.DateTime => EventPropertyKind.DateTime,
        StoredFieldKind.SizeT => EventPropertyKind.SizeT,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a packed StoredFieldKind.")
    };

    private (EventColumnChunk Chunk, int Row) Locate(int index)
    {
        int[] prefix = SealedPrefix();
        int chunk = FindChunk(prefix, index);

        return (_sealedChunks[chunk], index - prefix[chunk]);
    }

    private (EventColumnChunk Chunk, int Row) LocateSealed(int index) =>
        (uint)index >= (uint)_sealedCount ? throw new ArgumentOutOfRangeException(nameof(index),
            "The columnar representation exists only for sealed rows; check IsPending first.") :
        Locate(index);

    private ResolvedEvent Pending(int index) => _pendingTail[index - _sealedCount];

    private ImmutableArray<EventProperty> ReconstructEventDataValues(int index)
    {
        int fieldCount = RawEventDataCount(index);

        if (fieldCount == 0) { return default; }

        ImmutableArray<EventProperty>.Builder values = ImmutableArray.CreateBuilder<EventProperty>(fieldCount);

        for (int field = 0; field < fieldCount; field++)
        {
            values.Add(ReconstructEventProperty(RawEventDataField(index, field)));
        }

        return values.MoveToImmutable();
    }

    private string[] ReconstructKeywords(int index)
    {
        ReadOnlySpan<int> keywordIndices = RawKeywords(index);

        if (keywordIndices.Length == 0) { return []; }

        string[] keywords = new string[keywordIndices.Length];

        for (int i = 0; i < keywordIndices.Length; i++) { keywords[i] = PoolGet(keywordIndices[i])!; }

        return keywords;
    }

    private ResolvedEvent ReconstructLeanEvent(int index)
    {
        int userIdPoolIndex = RawPoolIndex(EventColumnField.UserId, index);
        long recordId = RawRecordId(index, out bool hasRecordId);
        Guid activityId = RawActivityId(index, out bool hasActivityId);
        int processId = RawProcessId(index, out bool hasProcessId);
        int threadId = RawThreadId(index, out bool hasThreadId);

        return new ResolvedEvent(
            PoolGet(RawPoolIndex(EventColumnField.OwningLog, index))!,
            (LogPathType)RawLogPathType(index))
        {
            Id = RawId(index),
            TimeCreated = new DateTime(RawTimeTicks(index), DateTimeKind.Utc),
            ComputerName = PoolGet(RawPoolIndex(EventColumnField.ComputerName, index)) ?? string.Empty,
            Description = PoolGet(RawPoolIndex(EventColumnField.Description, index)) ?? string.Empty,
            Level = PoolGet(RawPoolIndex(EventColumnField.Level, index)) ?? string.Empty,
            LogName = PoolGet(RawPoolIndex(EventColumnField.LogName, index)) ?? string.Empty,
            Source = PoolGet(RawPoolIndex(EventColumnField.Source, index)) ?? string.Empty,
            TaskCategory = PoolGet(RawPoolIndex(EventColumnField.TaskCategory, index)) ?? string.Empty,
            UserId = userIdPoolIndex < 0 ? null : new SecurityIdentifier(PoolGet(userIdPoolIndex)!),
            RecordId = hasRecordId ? recordId : null,
            ActivityId = hasActivityId ? activityId : null,
            ProcessId = hasProcessId ? processId : null,
            ThreadId = hasThreadId ? threadId : null,
            UserDataIncomplete = RawUserDataIncomplete(index),
            Keywords = ReconstructKeywords(index)
        };
    }

    private TemplateFieldSchema? ReconstructSchema(int index)
    {
        int schemaId = RawEventDataSchemaId(index);

        if (schemaId < 0) { return null; }

        ReadOnlySpan<int> nameIndices = SchemaFieldNameIndices(schemaId);
        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>(nameIndices.Length);

        foreach (int nameIndex in nameIndices) { names.Add(PoolGet(nameIndex)!); }

        // Both orderings resolve to the single stored matched-ordering names, so named access reproduces the original
        // regardless of whether the source matched its Visible or All ordering.
        ImmutableArray<string> matchedNames = names.MoveToImmutable();

        return new TemplateFieldSchema(matchedNames, matchedNames);
    }

    private ImmutableArray<UserDataField> ReconstructUserData(int index)
    {
        int fieldCount = RawUserDataCount(index);

        if (fieldCount == 0) { return default; }

        ImmutableArray<UserDataField>.Builder fields = ImmutableArray.CreateBuilder<UserDataField>(fieldCount);

        for (int field = 0; field < fieldCount; field++)
        {
            ReadOnlySpan<int> valueIndices = RawUserDataValues(index, field);
            ImmutableArray<string>.Builder values = ImmutableArray.CreateBuilder<string>(valueIndices.Length);

            foreach (int valueIndex in valueIndices) { values.Add(PoolGet(valueIndex)!); }

            fields.Add(new UserDataField(
                PoolGet(RawUserDataPathIndex(index, field))!,
                values.MoveToImmutable(),
                RawUserDataTruncated(index, field)));
        }

        return fields.MoveToImmutable();
    }

    private int[] SealedPrefix()
    {
        int[]? prefix = Volatile.Read(ref _sealedPrefix);

        if (prefix is not null) { return prefix; }

        prefix = new int[_sealedChunks.Count + 1];

        for (int i = 0; i < _sealedChunks.Count; i++) { prefix[i + 1] = prefix[i] + _sealedChunks[i].RowCount; }

        Volatile.Write(ref _sealedPrefix, prefix);

        return prefix;
    }
}

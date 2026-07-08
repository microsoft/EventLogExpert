// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;
using System.Runtime.InteropServices;

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
internal sealed class EventColumnStore
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
        long contentVersion)
    {
        _sealedChunks = sealedChunks;
        _pendingTail = pendingTail;
        _pool = pool;
        _schemas = schemas;
        _sealedCount = sealedCount;
        Generation = generation;
        ContentVersion = contentVersion;
    }

    /// <summary>Log-lifetime-monotonic ingest counter, bumped on every append and never reset across generations.</summary>
    internal long ContentVersion { get; }

    internal int Count => _sealedCount + _pendingTail.Count;

    /// <summary>Per-log reload counter, stable across appends and bumped only by a new-generation build.</summary>
    internal int Generation { get; }

    internal int PoolDistinctCount => _pool.DistinctCount;

    internal int SchemaCount => _schemas.Length;

    internal int SealedChunkCount => _sealedChunks.Count;

    internal int SealedCount => _sealedCount;

    /// <summary>
    ///     Columnarizes an initial batch into ceil(n / <see cref="TargetChunkSize" />) sealed chunks (empty pending
    ///     tail), stamping the snapshot with <paramref name="generation" /> and <paramref name="contentVersion" />.
    /// </summary>
    internal static EventColumnStore Build(IReadOnlyList<ResolvedEvent> batch, int generation, long contentVersion)
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

        return new EventColumnStore(chunks, ImmutableList<ResolvedEvent>.Empty, pool, schemas, batch.Count, generation, contentVersion);
    }

    /// <summary>
    ///     Returns a new snapshot with <paramref name="batch" /> appended to the pending tail; whenever the tail reaches
    ///     <see cref="TargetChunkSize" /> rows it seals the first N into one new columnar chunk (interning into the pool and
    ///     schema table). Bumps <see cref="ContentVersion" />, leaves <see cref="Generation" /> unchanged, and preserves every
    ///     prior global index. An empty batch is a no-op that returns the same instance.
    /// </summary>
    internal EventColumnStore Append(IReadOnlyList<ResolvedEvent> batch)
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

        return new EventColumnStore(chunks, pending, pool, schemas, sealedCount, Generation, ContentVersion + 1);
    }

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

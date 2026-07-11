// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Structured;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     The pooled scalar columns of an <see cref="EventColumnChunk" />, addressed by
///     <see cref="EventColumnChunk.RowPoolIndex" />.
/// </summary>
internal enum EventColumnField : byte
{
    OwningLog,
    ComputerName,
    Description,
    Level,
    LogName,
    Source,
    TaskCategory,
    Xml,
    UserId,
    Opcode
}

/// <summary>
///     A read view over one stored &lt;EventData&gt; field: its <see cref="StoredFieldKind" /> plus the slot the kind
///     actually uses (packed <see cref="Bits" />, a pool index in <see cref="RefIndex" />, a chunk-local byte range in
///     <see cref="Bytes" />, or a chunk-local pooled-value range in <see cref="ValueIndices" />). Spans point into the
///     owning chunk's immutable columns, so the view is only valid for as long as the chunk is reachable.
/// </summary>
internal readonly ref struct RawEventDataField
{
    internal RawEventDataField(StoredFieldKind kind, long bits, int refIndex, ReadOnlySpan<byte> bytes, ReadOnlySpan<int> valueIndices)
    {
        Kind = kind;
        Bits = bits;
        RefIndex = refIndex;
        Bytes = bytes;
        ValueIndices = valueIndices;
    }

    internal StoredFieldKind Kind { get; }

    internal long Bits { get; }

    internal int RefIndex { get; }

    internal ReadOnlySpan<byte> Bytes { get; }

    internal ReadOnlySpan<int> ValueIndices { get; }
}

/// <summary>
///     An immutable, self-contained columnar segment for a bounded batch of <see cref="ResolvedEvent" />s.
///     Row-aligned scalar arrays hold each row's packed and pooled values; per-row offset/count pairs address the chunk's
///     OWN variable-length side columns (keywords, UserData, EventData). All offsets are chunk-relative, so appending a
///     new chunk never grows or copies a shared buffer. Only the string pool and the EventData schema table are global;
///     they are threaded in as builders at build time and extended immutably by the store.
/// </summary>
internal sealed class EventColumnChunk
{
    private readonly Guid[] _activityId;
    private readonly bool[] _activityIdHas;
    private readonly int[] _computerName;
    private readonly int[] _description;
    private readonly int[] _eventDataCount;
    private readonly int[] _eventDataOffset;

    private readonly int[] _eventDataSchemaId;
    private readonly long[] _fieldBits;
    private readonly byte[] _fieldBytes;
    private readonly int[] _fieldBytesCount;
    private readonly int[] _fieldBytesOffset;
    private readonly byte[] _fieldKind;
    private readonly int[] _fieldRef;
    private readonly int[] _fieldValue;
    private readonly int[] _fieldValueCount;
    private readonly int[] _fieldValueOffset;

    private readonly int[] _id;
    private readonly int[] _keywordCount;

    private readonly int[] _keywordOffset;
    private readonly int[] _keywordValue;
    private readonly int[] _level;
    private readonly int[] _logName;
    private readonly byte[] _logPathType;
    private readonly int[] _opcode;
    private readonly int[] _owningLog;
    private readonly int[] _processId;
    private readonly bool[] _processIdHas;

    private readonly long[] _recordId;
    private readonly bool[] _recordIdHas;
    private readonly Guid[] _relatedActivityId;
    private readonly bool[] _relatedActivityIdHas;
    private readonly int[] _source;
    private readonly int[] _taskCategory;
    private readonly int[] _threadId;
    private readonly bool[] _threadIdHas;
    private readonly long[] _timeTicks;
    private readonly int[] _userDataCount;
    private readonly bool[] _userDataIncomplete;

    private readonly int[] _userDataOffset;
    private readonly int[] _userDataPath;
    private readonly bool[] _userDataTruncated;
    private readonly int[] _userDataValue;
    private readonly int[] _userDataValuesCount;
    private readonly int[] _userDataValuesOffset;
    private readonly int[] _userId;
    private readonly int[] _xml;

    private EventColumnChunk(Builder builder)
    {
        RowCount = builder.RowCount;

        _owningLog = builder.OwningLog;
        _computerName = builder.ComputerName;
        _description = builder.Description;
        _level = builder.Level;
        _logName = builder.LogName;
        _source = builder.Source;
        _taskCategory = builder.TaskCategory;
        _xml = builder.Xml;
        _userId = builder.UserId;
        _opcode = builder.Opcode;

        _id = builder.Id;
        _timeTicks = builder.TimeTicks;
        _logPathType = builder.LogPathType;
        _userDataIncomplete = builder.UserDataIncomplete;

        _recordId = builder.RecordId;
        _recordIdHas = builder.RecordIdHas;
        _activityId = builder.ActivityId;
        _activityIdHas = builder.ActivityIdHas;
        _relatedActivityId = builder.RelatedActivityId;
        _relatedActivityIdHas = builder.RelatedActivityIdHas;
        _processId = builder.ProcessId;
        _processIdHas = builder.ProcessIdHas;
        _threadId = builder.ThreadId;
        _threadIdHas = builder.ThreadIdHas;

        _keywordOffset = builder.KeywordOffset;
        _keywordCount = builder.KeywordCount;
        _keywordValue = [.. builder.KeywordValue];

        _userDataOffset = builder.UserDataOffset;
        _userDataCount = builder.UserDataCount;
        _userDataPath = [.. builder.UserDataPath];
        _userDataTruncated = [.. builder.UserDataTruncated];
        _userDataValuesOffset = [.. builder.UserDataValuesOffset];
        _userDataValuesCount = [.. builder.UserDataValuesCount];
        _userDataValue = [.. builder.UserDataValue];

        _eventDataSchemaId = builder.EventDataSchemaId;
        _eventDataOffset = builder.EventDataOffset;
        _eventDataCount = builder.EventDataCount;
        _fieldKind = [.. builder.FieldKind];
        _fieldBits = [.. builder.FieldBits];
        _fieldRef = [.. builder.FieldRef];
        _fieldBytesOffset = [.. builder.FieldBytesOffset];
        _fieldBytesCount = [.. builder.FieldBytesCount];
        _fieldBytes = [.. builder.FieldBytes];
        _fieldValueOffset = [.. builder.FieldValueOffset];
        _fieldValueCount = [.. builder.FieldValueCount];
        _fieldValue = [.. builder.FieldValue];
    }

    // Read-only views over the row-aligned scalar columns, for bulk column-scan materialization (a whole column copied
    // once per chunk rather than a per-row accessor call). The span is valid only while this immutable chunk is reachable.
    internal ReadOnlySpan<Guid> ActivityIdColumn => _activityId;

    internal ReadOnlySpan<bool> ActivityIdHasColumn => _activityIdHas;

    internal ReadOnlySpan<int> IdColumn => _id;

    internal ReadOnlySpan<int> ProcessIdColumn => _processId;

    internal ReadOnlySpan<bool> ProcessIdHasColumn => _processIdHas;

    internal ReadOnlySpan<long> RecordIdColumn => _recordId;

    internal ReadOnlySpan<bool> RecordIdHasColumn => _recordIdHas;

    internal ReadOnlySpan<Guid> RelatedActivityIdColumn => _relatedActivityId;

    internal ReadOnlySpan<bool> RelatedActivityIdHasColumn => _relatedActivityIdHas;

    internal int RowCount { get; }

    internal ReadOnlySpan<int> ThreadIdColumn => _threadId;

    internal ReadOnlySpan<bool> ThreadIdHasColumn => _threadIdHas;

    internal ReadOnlySpan<long> TimeTicksColumn => _timeTicks;

    internal static EventColumnChunk Build(
        IReadOnlyList<ResolvedEvent> events,
        int start,
        int count,
        EventColumnPool.Builder poolBuilder,
        EventDataSchemaBuilder schemaBuilder)
    {
        Builder builder = new(count);

        for (int row = 0; row < count; row++)
        {
            builder.AddRow(events[start + row], poolBuilder, schemaBuilder);
        }

        return new EventColumnChunk(builder);
    }

    /// <summary>The raw pool-index column for <paramref name="column" />, one entry per row (-1 = null).</summary>
    internal ReadOnlySpan<int> PoolIndexColumn(EventColumnField column) => column switch
    {
        EventColumnField.OwningLog => _owningLog,
        EventColumnField.ComputerName => _computerName,
        EventColumnField.Description => _description,
        EventColumnField.Level => _level,
        EventColumnField.LogName => _logName,
        EventColumnField.Source => _source,
        EventColumnField.TaskCategory => _taskCategory,
        EventColumnField.Xml => _xml,
        EventColumnField.UserId => _userId,
        EventColumnField.Opcode => _opcode,
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, null)
    };

    internal Guid RowActivityId(int row, out bool hasValue)
    {
        hasValue = _activityIdHas[row];

        return _activityId[row];
    }

    internal int RowEventDataCount(int row) => _eventDataCount[row];

    internal RawEventDataField RowEventDataField(int row, int field)
    {
        int fieldIndex = _eventDataOffset[row] + field;

        return new RawEventDataField(
            (StoredFieldKind)_fieldKind[fieldIndex],
            _fieldBits[fieldIndex],
            _fieldRef[fieldIndex],
            _fieldBytes.AsSpan(_fieldBytesOffset[fieldIndex], _fieldBytesCount[fieldIndex]),
            _fieldValue.AsSpan(_fieldValueOffset[fieldIndex], _fieldValueCount[fieldIndex]));
    }

    internal int RowEventDataSchemaId(int row) => _eventDataSchemaId[row];

    internal int RowId(int row) => _id[row];

    internal int RowKeywordCount(int row) => _keywordCount[row];

    internal ReadOnlySpan<int> RowKeywords(int row) => _keywordValue.AsSpan(_keywordOffset[row], _keywordCount[row]);

    internal byte RowLogPathType(int row) => _logPathType[row];

    internal int RowPoolIndex(EventColumnField column, int row) => column switch
    {
        EventColumnField.OwningLog => _owningLog[row],
        EventColumnField.ComputerName => _computerName[row],
        EventColumnField.Description => _description[row],
        EventColumnField.Level => _level[row],
        EventColumnField.LogName => _logName[row],
        EventColumnField.Source => _source[row],
        EventColumnField.TaskCategory => _taskCategory[row],
        EventColumnField.Xml => _xml[row],
        EventColumnField.UserId => _userId[row],
        EventColumnField.Opcode => _opcode[row],
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, null)
    };

    internal int RowProcessId(int row, out bool hasValue)
    {
        hasValue = _processIdHas[row];

        return _processId[row];
    }

    internal long RowRecordId(int row, out bool hasValue)
    {
        hasValue = _recordIdHas[row];

        return _recordId[row];
    }

    internal Guid RowRelatedActivityId(int row, out bool hasValue)
    {
        hasValue = _relatedActivityIdHas[row];

        return _relatedActivityId[row];
    }

    internal int RowThreadId(int row, out bool hasValue)
    {
        hasValue = _threadIdHas[row];

        return _threadId[row];
    }

    internal long RowTimeTicks(int row) => _timeTicks[row];

    internal int RowUserDataCount(int row) => _userDataCount[row];

    internal bool RowUserDataIncomplete(int row) => _userDataIncomplete[row];

    internal int RowUserDataPathIndex(int row, int field) => _userDataPath[_userDataOffset[row] + field];

    internal bool RowUserDataTruncated(int row, int field) => _userDataTruncated[_userDataOffset[row] + field];

    internal ReadOnlySpan<int> RowUserDataValues(int row, int field)
    {
        int fieldIndex = _userDataOffset[row] + field;

        return _userDataValue.AsSpan(_userDataValuesOffset[fieldIndex], _userDataValuesCount[fieldIndex]);
    }

    private static StoredFieldKind MapPackedKind(EventPropertyKind kind) => kind switch
    {
        EventPropertyKind.SByte => StoredFieldKind.SByte,
        EventPropertyKind.Byte => StoredFieldKind.Byte,
        EventPropertyKind.Int16 => StoredFieldKind.Int16,
        EventPropertyKind.UInt16 => StoredFieldKind.UInt16,
        EventPropertyKind.Int32 => StoredFieldKind.Int32,
        EventPropertyKind.UInt32 => StoredFieldKind.UInt32,
        EventPropertyKind.Int64 => StoredFieldKind.Int64,
        EventPropertyKind.UInt64 => StoredFieldKind.UInt64,
        EventPropertyKind.Single => StoredFieldKind.Single,
        EventPropertyKind.Double => StoredFieldKind.Double,
        EventPropertyKind.Boolean => StoredFieldKind.Boolean,
        EventPropertyKind.DateTime => StoredFieldKind.DateTime,
        EventPropertyKind.SizeT => StoredFieldKind.SizeT,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Reference kind is not a packed StoredFieldKind.")
    };

    // Mirrors EventDataView.TryGetOrdering: the stored values align with either the visible or the all-names ordering,
    // chosen by length; a default result means no named schema applies (values remain positional).
    private static ImmutableArray<string> ResolveOrderingNames(ResolvedEvent resolvedEvent)
    {
        TemplateFieldSchema? schema = resolvedEvent.EventDataSchema;
        ImmutableArray<EventProperty> values = resolvedEvent.EventDataValues;

        if (schema is null || values.IsDefaultOrEmpty) { return default; }

        if (values.Length == schema.VisibleNames.Length) { return schema.VisibleNames; }

        return values.Length == schema.AllNames.Length ? schema.AllNames : default;
    }

    private sealed class Builder
    {
        internal readonly Guid[] ActivityId;
        internal readonly bool[] ActivityIdHas;
        internal readonly int[] ComputerName;
        internal readonly int[] Description;
        internal readonly int[] EventDataCount;
        internal readonly int[] EventDataOffset;

        internal readonly int[] EventDataSchemaId;
        internal readonly List<long> FieldBits = [];
        internal readonly List<byte> FieldBytes = [];
        internal readonly List<int> FieldBytesCount = [];
        internal readonly List<int> FieldBytesOffset = [];
        internal readonly List<byte> FieldKind = [];
        internal readonly List<int> FieldRef = [];
        internal readonly List<int> FieldValue = [];
        internal readonly List<int> FieldValueCount = [];
        internal readonly List<int> FieldValueOffset = [];

        internal readonly int[] Id;
        internal readonly int[] KeywordCount;

        internal readonly int[] KeywordOffset;
        internal readonly List<int> KeywordValue = [];
        internal readonly int[] Level;
        internal readonly int[] LogName;
        internal readonly byte[] LogPathType;
        internal readonly int[] Opcode;
        internal readonly int[] OwningLog;
        internal readonly int[] ProcessId;
        internal readonly bool[] ProcessIdHas;

        internal readonly long[] RecordId;
        internal readonly bool[] RecordIdHas;
        internal readonly Guid[] RelatedActivityId;
        internal readonly bool[] RelatedActivityIdHas;
        internal readonly int[] Source;
        internal readonly int[] TaskCategory;
        internal readonly int[] ThreadId;
        internal readonly bool[] ThreadIdHas;
        internal readonly long[] TimeTicks;
        internal readonly int[] UserDataCount;
        internal readonly bool[] UserDataIncomplete;

        internal readonly int[] UserDataOffset;
        internal readonly List<int> UserDataPath = [];
        internal readonly List<bool> UserDataTruncated = [];
        internal readonly List<int> UserDataValue = [];
        internal readonly List<int> UserDataValuesCount = [];
        internal readonly List<int> UserDataValuesOffset = [];
        internal readonly int[] UserId;
        internal readonly int[] Xml;

        private int _row;

        internal Builder(int count)
        {
            OwningLog = new int[count];
            ComputerName = new int[count];
            Description = new int[count];
            Level = new int[count];
            LogName = new int[count];
            Source = new int[count];
            TaskCategory = new int[count];
            Xml = new int[count];
            UserId = new int[count];
            Opcode = new int[count];

            Id = new int[count];
            TimeTicks = new long[count];
            LogPathType = new byte[count];
            UserDataIncomplete = new bool[count];

            RecordId = new long[count];
            RecordIdHas = new bool[count];
            ActivityId = new Guid[count];
            ActivityIdHas = new bool[count];
            RelatedActivityId = new Guid[count];
            RelatedActivityIdHas = new bool[count];
            ProcessId = new int[count];
            ProcessIdHas = new bool[count];
            ThreadId = new int[count];
            ThreadIdHas = new bool[count];

            KeywordOffset = new int[count];
            KeywordCount = new int[count];

            UserDataOffset = new int[count];
            UserDataCount = new int[count];

            EventDataSchemaId = new int[count];
            EventDataOffset = new int[count];
            EventDataCount = new int[count];
        }

        internal int RowCount => _row;

        internal void AddRow(ResolvedEvent resolvedEvent, EventColumnPool.Builder pool, EventDataSchemaBuilder schemas)
        {
            int row = _row;

            OwningLog[row] = pool.Intern(resolvedEvent.OwningLog);
            ComputerName[row] = pool.Intern(resolvedEvent.ComputerName);
            Description[row] = pool.Intern(resolvedEvent.Description);
            Level[row] = pool.Intern(resolvedEvent.Level);
            LogName[row] = pool.Intern(resolvedEvent.LogName);
            Source[row] = pool.Intern(resolvedEvent.Source);
            TaskCategory[row] = pool.Intern(resolvedEvent.TaskCategory);
            Xml[row] = pool.Intern(resolvedEvent.Xml);
            UserId[row] = pool.Intern(resolvedEvent.UserId?.Value);
            Opcode[row] = pool.Intern(resolvedEvent.Opcode);

            Id[row] = resolvedEvent.Id;
            TimeTicks[row] = resolvedEvent.TimeCreated.Ticks;
            LogPathType[row] = (byte)resolvedEvent.LogPathType;
            UserDataIncomplete[row] = resolvedEvent.UserDataIncomplete;

            if (resolvedEvent.RecordId is { } recordId) { RecordId[row] = recordId; RecordIdHas[row] = true; }

            if (resolvedEvent.ActivityId is { } activityId) { ActivityId[row] = activityId; ActivityIdHas[row] = true; }

            if (resolvedEvent.RelatedActivityId is { } relatedActivityId) { RelatedActivityId[row] = relatedActivityId; RelatedActivityIdHas[row] = true; }

            if (resolvedEvent.ProcessId is { } processId) { ProcessId[row] = processId; ProcessIdHas[row] = true; }

            if (resolvedEvent.ThreadId is { } threadId) { ThreadId[row] = threadId; ThreadIdHas[row] = true; }

            AddKeywords(row, resolvedEvent, pool);
            AddUserData(row, resolvedEvent, pool);
            AddEventData(row, resolvedEvent, pool, schemas);

            _row++;
        }

        private static int InternSchema(ResolvedEvent resolvedEvent, EventColumnPool.Builder pool, EventDataSchemaBuilder schemas)
        {
            ImmutableArray<string> names = ResolveOrderingNames(resolvedEvent);

            if (names.IsDefault) { return -1; }

            int[] fieldNameIndices = new int[names.Length];

            for (int i = 0; i < names.Length; i++) { fieldNameIndices[i] = pool.Intern(names[i]); }

            return schemas.Intern(fieldNameIndices);
        }

        private void AddEventData(int row, ResolvedEvent resolvedEvent, EventColumnPool.Builder pool, EventDataSchemaBuilder schemas)
        {
            EventDataOffset[row] = FieldKind.Count;

            ImmutableArray<EventProperty> values = resolvedEvent.EventDataValues;

            if (values.IsDefaultOrEmpty)
            {
                EventDataSchemaId[row] = -1;
                EventDataCount[row] = 0;

                return;
            }

            EventDataSchemaId[row] = InternSchema(resolvedEvent, pool, schemas);

            foreach (EventProperty property in values) { AddEventDataField(property, pool); }

            EventDataCount[row] = values.Length;
        }

        private void AddEventDataField(EventProperty property, EventColumnPool.Builder pool)
        {
            if (property.Kind != EventPropertyKind.Reference)
            {
                AppendField(MapPackedKind(property.Kind), property.PackedBits, refIndex: -1);

                return;
            }

            object? reference = property.Reference;

            switch (reference)
            {
                case null:
                    AppendField(StoredFieldKind.Null, bits: 0, refIndex: -1);
                    return;
                case string text:
                    AppendField(StoredFieldKind.String, bits: 0, refIndex: pool.Intern(text));
                    return;
                case SecurityIdentifier sid:
                    AppendField(StoredFieldKind.Sid, bits: 0, refIndex: pool.Intern(sid.Value));
                    return;
                case Guid guid:
                    AppendBytesField(StoredFieldKind.Guid, guid.ToByteArray());
                    return;
                case string[] stringArray:
                    AppendStringArrayField(stringArray, pool);
                    return;
            }

            // Numeric arrays must be matched by EXACT runtime type: byte[]/sbyte[], ushort[]/short[], and int[]/uint[]
            // are pairwise assignment-compatible, so an `is uint[]` type pattern would also capture an int[] (and back).
            Type referenceType = reference.GetType();

            if (referenceType == typeof(byte[]))
            {
                AppendBytesField(StoredFieldKind.Bytes, (byte[])reference);
            }
            else if (referenceType == typeof(ushort[]))
            {
                AppendBytesField(StoredFieldKind.UInt16Array, MemoryMarshal.AsBytes(((ushort[])reference).AsSpan()));
            }
            else if (referenceType == typeof(uint[]))
            {
                AppendBytesField(StoredFieldKind.UInt32Array, MemoryMarshal.AsBytes(((uint[])reference).AsSpan()));
            }
            else if (referenceType == typeof(int[]))
            {
                AppendBytesField(StoredFieldKind.Int32Array, MemoryMarshal.AsBytes(((int[])reference).AsSpan()));
            }
            else
            {
                AppendField(StoredFieldKind.StringForm, bits: 0, refIndex: pool.Intern(EventFieldValue.FromProperty(property).AsString()));
            }
        }

        private void AddKeywords(int row, ResolvedEvent resolvedEvent, EventColumnPool.Builder pool)
        {
            KeywordOffset[row] = KeywordValue.Count;

            IReadOnlyList<string> keywords = resolvedEvent.Keywords;

            for (int i = 0; i < keywords.Count; i++) { KeywordValue.Add(pool.Intern(keywords[i])); }

            KeywordCount[row] = keywords.Count;
        }

        private void AddUserData(int row, ResolvedEvent resolvedEvent, EventColumnPool.Builder pool)
        {
            UserDataOffset[row] = UserDataPath.Count;

            ImmutableArray<UserDataField> fields = resolvedEvent.UserData;

            if (fields.IsDefaultOrEmpty)
            {
                UserDataCount[row] = 0;

                return;
            }

            foreach ((string path, ImmutableArray<string> values, bool isTruncated) in fields)
            {
                UserDataPath.Add(pool.Intern(path));
                UserDataTruncated.Add(isTruncated);
                UserDataValuesOffset.Add(UserDataValue.Count);

                int valueCount = values.IsDefault ? 0 : values.Length;

                for (int i = 0; i < valueCount; i++) { UserDataValue.Add(pool.Intern(values[i])); }

                UserDataValuesCount.Add(valueCount);
            }

            UserDataCount[row] = fields.Length;
        }

        private void AppendBytesField(StoredFieldKind kind, ReadOnlySpan<byte> bytes)
        {
            FieldKind.Add((byte)kind);
            FieldBits.Add(0);
            FieldRef.Add(-1);
            FieldBytesOffset.Add(FieldBytes.Count);
            FieldBytesCount.Add(bytes.Length);
            FieldBytes.AddRange(bytes);
            FieldValueOffset.Add(FieldValue.Count);
            FieldValueCount.Add(0);
        }

        private void AppendField(StoredFieldKind kind, long bits, int refIndex)
        {
            FieldKind.Add((byte)kind);
            FieldBits.Add(bits);
            FieldRef.Add(refIndex);
            FieldBytesOffset.Add(FieldBytes.Count);
            FieldBytesCount.Add(0);
            FieldValueOffset.Add(FieldValue.Count);
            FieldValueCount.Add(0);
        }

        private void AppendStringArrayField(string[] values, EventColumnPool.Builder pool)
        {
            FieldKind.Add((byte)StoredFieldKind.StringArray);
            FieldBits.Add(0);
            FieldRef.Add(-1);
            FieldBytesOffset.Add(FieldBytes.Count);
            FieldBytesCount.Add(0);
            FieldValueOffset.Add(FieldValue.Count);

            foreach (string value in values) { FieldValue.Add(pool.Intern(value)); }

            FieldValueCount.Add(values.Length);
        }
    }
}

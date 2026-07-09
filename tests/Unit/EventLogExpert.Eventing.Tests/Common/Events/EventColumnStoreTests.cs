// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventColumnStoreTests
{
    [Fact]
    public void Append_BatchBelowChunkSize_GrowsCountKeepsSealedCount()
    {
        EventColumnStore store = EventColumnStore.Build([], generation: 1, contentVersion: 5);

        EventColumnStore appended = store.Append([Event(0), Event(1)]);

        Assert.Equal(0, appended.SealedCount);
        Assert.Equal(2, appended.Count);
        Assert.True(appended.IsPending(0));
        Assert.True(appended.IsPending(1));
        Assert.Equal(6, appended.ContentVersion);
        Assert.Equal(1, appended.Generation);

        // The pending tail reads straight off the ResolvedEvent, preserving physical order.
        Assert.Equal(0, appended.RawId(0));
        Assert.Equal(1, appended.RawId(1));
    }

    [Fact]
    public void Append_CrossingChunkSize_SealsOneChunkAndPreservesGlobalIndex()
    {
        const int ChunkSize = 4096;

        ResolvedEvent[] belowThreshold = new ResolvedEvent[ChunkSize - 1];
        for (int i = 0; i < belowThreshold.Length; i++) { belowThreshold[i] = Event(i); }

        EventColumnStore beforeSeal = EventColumnStore.Build([], generation: 2, contentVersion: 10).Append(belowThreshold);

        Assert.Equal(0, beforeSeal.SealedCount);
        Assert.Equal(ChunkSize - 1, beforeSeal.Count);
        Assert.Equal(ChunkSize - 2, beforeSeal.RawId(ChunkSize - 2));

        EventColumnStore afterSeal = beforeSeal.Append([Event(ChunkSize - 1), Event(ChunkSize)]);

        Assert.Equal(1, afterSeal.SealedChunkCount);
        Assert.Equal(ChunkSize, afterSeal.SealedCount);
        Assert.Equal(ChunkSize + 1, afterSeal.Count);
        Assert.Equal(1, afterSeal.Count - afterSeal.SealedCount);
        Assert.True(afterSeal.ContentVersion > beforeSeal.ContentVersion);
        Assert.Equal(beforeSeal.Generation, afterSeal.Generation);

        // A row that was pending at global index chunkSize - 2 is now sealed at the SAME global index with the same value.
        Assert.False(afterSeal.IsPending(ChunkSize - 2));
        Assert.Equal(ChunkSize - 2, afterSeal.RawId(ChunkSize - 2));
    }

    [Fact]
    public void Append_EmptyBatch_ReturnsSameStore()
    {
        EventColumnStore store = EventColumnStore.Build([Event(0)], generation: 1, contentVersion: 3);

        EventColumnStore appended = store.Append([]);

        Assert.Same(store, appended);
        Assert.Equal(3, appended.ContentVersion);
        Assert.Equal(1, appended.Count);
    }

    [Fact]
    public void Append_ToEmptyStore_InitializesMinMaxFromBatch()
    {
        long a = new DateTime(2020, 5, 5, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long b = new DateTime(2020, 5, 6, 0, 0, 0, DateTimeKind.Utc).Ticks;

        EventColumnStore store = EventColumnStore.Build([], generation: 0, contentVersion: 0)
            .Append([EventAt(0, b), EventAt(1, a)]);

        Assert.True(store.TryGetTimeRange(out long minTicks, out long maxTicks));
        Assert.Equal(a, minTicks);
        Assert.Equal(b, maxTicks);
    }

    [Fact]
    public void Append_WidensMinMaxForOutOfRangeEventsAndLeavesInRangeUnchanged()
    {
        long earlier = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long early = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long within = new DateTime(2021, 6, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long late = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long later = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        EventColumnStore seed = EventColumnStore.Build([EventAt(0, early), EventAt(1, late)], generation: 1, contentVersion: 1);
        Assert.Equal(early, seed.MinTimeTicks);
        Assert.Equal(late, seed.MaxTimeTicks);

        // Appending an older AND a newer event widens both ends.
        EventColumnStore widened = seed.Append([EventAt(2, earlier), EventAt(3, later)]);
        Assert.Equal(earlier, widened.MinTimeTicks);
        Assert.Equal(later, widened.MaxTimeTicks);

        // Appending an in-range event leaves the span unchanged.
        EventColumnStore unchanged = widened.Append([EventAt(4, within)]);
        Assert.Equal(earlier, unchanged.MinTimeTicks);
        Assert.Equal(later, unchanged.MaxTimeTicks);
    }

    [Fact]
    public void Build_AbsentNullableScalars_HasFlagsFalse()
    {
        ResolvedEvent resolvedEvent = new("live", LogPathType.Channel);

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        store.RawRecordId(0, out bool hasRecordId);
        store.RawActivityId(0, out bool hasActivityId);
        store.RawProcessId(0, out bool hasProcessId);
        store.RawThreadId(0, out bool hasThreadId);

        Assert.False(hasRecordId);
        Assert.False(hasActivityId);
        Assert.False(hasProcessId);
        Assert.False(hasThreadId);
        Assert.Equal(-1, store.RawPoolIndex(EventColumnField.UserId, 0));
    }

    [Fact]
    public void Build_DistinctStrings_PoolDedupesAndAssignsStableIndices()
    {
        ResolvedEvent first = new("live", LogPathType.Channel) { Level = "Info" };
        ResolvedEvent second = new("live", LogPathType.Channel) { Level = "Info" };

        EventColumnStore store = EventColumnStore.Build([first, second], generation: 1, contentVersion: 1);

        // Same string interns to the same index across rows and across columns (empty string is shared).
        Assert.Equal(store.RawPoolIndex(EventColumnField.OwningLog, 0), store.RawPoolIndex(EventColumnField.OwningLog, 1));
        Assert.Equal(store.RawPoolIndex(EventColumnField.Level, 0), store.RawPoolIndex(EventColumnField.Level, 1));
        Assert.Equal(store.RawPoolIndex(EventColumnField.Description, 0), store.RawPoolIndex(EventColumnField.LogName, 0));

        // A null value (no UserId) interns to -1 and reads back as null.
        Assert.Equal(-1, store.RawPoolIndex(EventColumnField.UserId, 0));
        Assert.Null(store.PoolGet(-1));

        // Only the distinct strings {"live", "", "Info"} are pooled.
        Assert.Equal(3, store.PoolDistinctCount);
    }

    [Fact]
    public void Build_EventDataBlittableKinds_StoreBytes()
    {
        Guid guid = Guid.NewGuid();

        ResolvedEvent resolvedEvent = EventWithProperties(
            ("guid", guid),
            ("bytes", (EventProperty)(byte[])[1, 2, 3]),
            ("u16", EventProperty.FromReference(new ushort[] { 7, 8, 9 })),
            ("u32", EventProperty.FromReference(new uint[] { 10, 11 })),
            ("i32", EventProperty.FromReference(new[] { -1, -2 })));

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        RawEventDataField guidField = store.RawEventDataField(0, 0);
        Assert.Equal(StoredFieldKind.Guid, guidField.Kind);
        Assert.Equal(guid, new Guid(guidField.Bytes));

        RawEventDataField bytesField = store.RawEventDataField(0, 1);
        Assert.Equal(StoredFieldKind.Bytes, bytesField.Kind);
        Assert.Equal([1, 2, 3], bytesField.Bytes.ToArray());

        RawEventDataField uint16Field = store.RawEventDataField(0, 2);
        Assert.Equal(StoredFieldKind.UInt16Array, uint16Field.Kind);
        Assert.Equal([7, 8, 9], MemoryMarshal.Cast<byte, ushort>(uint16Field.Bytes).ToArray());

        RawEventDataField uint32Field = store.RawEventDataField(0, 3);
        Assert.Equal(StoredFieldKind.UInt32Array, uint32Field.Kind);
        Assert.Equal([10u, 11u], MemoryMarshal.Cast<byte, uint>(uint32Field.Bytes).ToArray());

        RawEventDataField int32Field = store.RawEventDataField(0, 4);
        Assert.Equal(StoredFieldKind.Int32Array, int32Field.Kind);
        Assert.Equal([-1, -2], MemoryMarshal.Cast<byte, int>(int32Field.Bytes).ToArray());
    }

    [Fact]
    public void Build_EventDataNullReference_StoresNullKind()
    {
        ResolvedEvent resolvedEvent = EventWithProperties(("missing", EventProperty.FromReference(null)));

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        Assert.Equal(StoredFieldKind.Null, store.RawEventDataField(0, 0).Kind);
    }

    [Fact]
    public void Build_EventDataPackedKinds_StoreKindAndBits()
    {
        DateTime time = new(2022, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        ResolvedEvent resolvedEvent = EventWithProperties(
            ("f0", (sbyte)-5),
            ("f1", (byte)200),
            ("f2", (short)-300),
            ("f3", (ushort)400),
            ("f4", -70000),
            ("f5", 80000u),
            ("f6", -5_000_000_000L),
            ("f7", 9_000_000_000UL),
            ("f8", 3.5f),
            ("f9", 2.5d),
            ("f10", true),
            ("f11", time),
            ("f12", (nuint)77));

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        Assert.Equal(13, store.RawEventDataCount(0));

        AssertKind(store, 0, StoredFieldKind.SByte);
        Assert.Equal((sbyte)-5, (sbyte)store.RawEventDataField(0, 0).Bits);

        AssertKind(store, 1, StoredFieldKind.Byte);
        Assert.Equal((byte)200, (byte)store.RawEventDataField(0, 1).Bits);

        AssertKind(store, 2, StoredFieldKind.Int16);
        Assert.Equal((short)-300, (short)store.RawEventDataField(0, 2).Bits);

        AssertKind(store, 3, StoredFieldKind.UInt16);
        Assert.Equal((ushort)400, (ushort)store.RawEventDataField(0, 3).Bits);

        AssertKind(store, 4, StoredFieldKind.Int32);
        Assert.Equal(-70000, (int)store.RawEventDataField(0, 4).Bits);

        AssertKind(store, 5, StoredFieldKind.UInt32);
        Assert.Equal(80000u, (uint)store.RawEventDataField(0, 5).Bits);

        AssertKind(store, 6, StoredFieldKind.Int64);
        Assert.Equal(-5_000_000_000L, store.RawEventDataField(0, 6).Bits);

        AssertKind(store, 7, StoredFieldKind.UInt64);
        Assert.Equal(9_000_000_000UL, unchecked((ulong)store.RawEventDataField(0, 7).Bits));

        AssertKind(store, 8, StoredFieldKind.Single);
        Assert.Equal(3.5f, BitConverter.Int32BitsToSingle((int)store.RawEventDataField(0, 8).Bits));

        AssertKind(store, 9, StoredFieldKind.Double);
        Assert.Equal(2.5d, BitConverter.Int64BitsToDouble(store.RawEventDataField(0, 9).Bits));

        AssertKind(store, 10, StoredFieldKind.Boolean);
        Assert.True(store.RawEventDataField(0, 10).Bits != 0);

        AssertKind(store, 11, StoredFieldKind.DateTime);
        Assert.Equal(time, DateTime.FromBinary(store.RawEventDataField(0, 11).Bits));

        AssertKind(store, 12, StoredFieldKind.SizeT);
        Assert.Equal(77UL, unchecked((ulong)store.RawEventDataField(0, 12).Bits));
    }

    [Fact]
    public void Build_EventDataPooledStringKinds_StorePoolIndex()
    {
        ResolvedEvent resolvedEvent = EventWithProperties(
            ("text", (EventProperty)"hello"),
            ("sid", (EventProperty)new SecurityIdentifier("S-1-5-18")),
            ("unknown", EventProperty.FromReference(new UnknownReference())));

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        RawEventDataField text = store.RawEventDataField(0, 0);
        Assert.Equal(StoredFieldKind.String, text.Kind);
        Assert.Equal("hello", store.PoolGet(text.RefIndex));

        RawEventDataField sid = store.RawEventDataField(0, 1);
        Assert.Equal(StoredFieldKind.Sid, sid.Kind);
        Assert.Equal("S-1-5-18", store.PoolGet(sid.RefIndex));

        RawEventDataField unknown = store.RawEventDataField(0, 2);
        Assert.Equal(StoredFieldKind.StringForm, unknown.Kind);
        Assert.Equal("unknown-ref", store.PoolGet(unknown.RefIndex));
    }

    [Fact]
    public void Build_EventDataStringArray_StorePooledValueIndices()
    {
        ResolvedEvent resolvedEvent = EventWithProperties(("names", (EventProperty)(string[])["alice", "bob"]));

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        RawEventDataField field = store.RawEventDataField(0, 0);
        Assert.Equal(StoredFieldKind.StringArray, field.Kind);
        Assert.Equal(2, field.ValueIndices.Length);
        Assert.Equal("alice", store.PoolGet(field.ValueIndices[0]));
        Assert.Equal("bob", store.PoolGet(field.ValueIndices[1]));
    }

    [Fact]
    public void Build_EventWithoutEventData_StoresNoSchemaAndZeroCount()
    {
        ResolvedEvent withData = new ResolvedEvent("live", LogPathType.Channel).WithEventData(("A", "x"));
        ResolvedEvent without = new("live", LogPathType.Channel);

        EventColumnStore store = EventColumnStore.Build([withData, without], generation: 1, contentVersion: 1);

        Assert.NotEqual(-1, store.RawEventDataSchemaId(0));
        Assert.Equal(1, store.RawEventDataCount(0));
        Assert.Equal(-1, store.RawEventDataSchemaId(1));
        Assert.Equal(0, store.RawEventDataCount(1));
    }

    [Fact]
    public void Build_ExceedingChunkSize_RoutesAcrossChunksAndPoolSegments()
    {
        const int ChunkSize = 4096;
        ResolvedEvent[] events = new ResolvedEvent[ChunkSize + 2];

        for (int i = 0; i < events.Length; i++)
        {
            // Second-chunk rows carry a distinct pooled Level so the pool spans more than one segment.
            events[i] = new ResolvedEvent("live", LogPathType.Channel) { Id = i, Level = i < ChunkSize ? "Info" : "Error" };
        }

        EventColumnStore store = EventColumnStore.Build(events, generation: 1, contentVersion: 1);

        Assert.Equal(2, store.SealedChunkCount);
        Assert.Equal(ChunkSize + 2, store.Count);

        // FindChunk binary search over 2 chunks + multi-segment pool Get both resolve.
        Assert.Equal(0, store.RawId(0));
        Assert.Equal(ChunkSize + 1, store.RawId(ChunkSize + 1));
        Assert.Equal("Info", store.PoolGet(store.RawPoolIndex(EventColumnField.Level, 0)));
        Assert.Equal("Error", store.PoolGet(store.RawPoolIndex(EventColumnField.Level, ChunkSize + 1)));
    }

    [Fact]
    public void Build_Keywords_RoundTripsOffsetCountAndValues()
    {
        ResolvedEvent resolvedEvent = new("live", LogPathType.Channel)
        {
            Keywords = ["Audit Success", "Classic"]
        };

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        Assert.Equal(2, store.RawKeywordCount(0));

        ReadOnlySpan<int> keywords = store.RawKeywords(0);
        Assert.Equal(2, keywords.Length);
        Assert.Equal("Audit Success", store.PoolGet(keywords[0]));
        Assert.Equal("Classic", store.PoolGet(keywords[1]));
    }

    [Fact]
    public void Build_MinMax_SpanTheBatchTickRange()
    {
        long early = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long middle = new DateTime(2021, 6, 15, 12, 0, 0, DateTimeKind.Utc).Ticks;
        long late = new DateTime(2022, 12, 31, 23, 59, 59, DateTimeKind.Utc).Ticks;

        // Deliberately unsorted so the range cannot be read off the first or last physical row.
        EventColumnStore store = EventColumnStore.Build(
            [EventAt(0, middle), EventAt(1, late), EventAt(2, early)], generation: 1, contentVersion: 1);

        Assert.True(store.TryGetTimeRange(out long minTicks, out long maxTicks));
        Assert.Equal(early, minTicks);
        Assert.Equal(late, maxTicks);
        Assert.Equal(early, store.MinTimeTicks);
        Assert.Equal(late, store.MaxTimeTicks);
    }

    [Fact]
    public void Build_SameFieldNames_ShareSchemaId()
    {
        ResolvedEvent first = new ResolvedEvent("live", LogPathType.Channel).WithEventData(("A", "x"), ("B", "y"));
        ResolvedEvent second = new ResolvedEvent("live", LogPathType.Channel).WithEventData(("A", "p"), ("B", "q"));
        ResolvedEvent other = new ResolvedEvent("live", LogPathType.Channel).WithEventData(("C", "z"));

        EventColumnStore store = EventColumnStore.Build([first, second, other], generation: 1, contentVersion: 1);

        Assert.Equal(store.RawEventDataSchemaId(0), store.RawEventDataSchemaId(1));
        Assert.NotEqual(store.RawEventDataSchemaId(0), store.RawEventDataSchemaId(2));
        Assert.Equal(2, store.SchemaCount);
    }

    [Fact]
    public void Build_ScalarColumns_MatchEvent()
    {
        DateTime time = new(2021, 6, 15, 10, 20, 30, DateTimeKind.Utc);
        Guid activity = Guid.NewGuid();

        ResolvedEvent resolvedEvent = new("live", LogPathType.Channel)
        {
            Id = 4624,
            ComputerName = "PC1",
            Description = "desc",
            Level = "Information",
            LogName = "Security",
            Source = "Auth",
            TaskCategory = "Logon",
            Xml = "<x/>",
            UserId = new SecurityIdentifier("S-1-5-18"),
            TimeCreated = time,
            RecordId = 99L,
            ActivityId = activity,
            ProcessId = 1234,
            ThreadId = 5678
        };

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        Assert.Equal(1, store.Count);
        Assert.Equal(1, store.SealedCount);
        Assert.False(store.IsPending(0));

        Assert.Equal(4624, store.RawId(0));
        Assert.Equal(time.Ticks, store.RawTimeTicks(0));
        Assert.Equal((byte)LogPathType.Channel, store.RawLogPathType(0));

        Assert.Equal("live", store.PoolGet(store.RawPoolIndex(EventColumnField.OwningLog, 0)));
        Assert.Equal("PC1", store.PoolGet(store.RawPoolIndex(EventColumnField.ComputerName, 0)));
        Assert.Equal("desc", store.PoolGet(store.RawPoolIndex(EventColumnField.Description, 0)));
        Assert.Equal("Information", store.PoolGet(store.RawPoolIndex(EventColumnField.Level, 0)));
        Assert.Equal("Security", store.PoolGet(store.RawPoolIndex(EventColumnField.LogName, 0)));
        Assert.Equal("Auth", store.PoolGet(store.RawPoolIndex(EventColumnField.Source, 0)));
        Assert.Equal("Logon", store.PoolGet(store.RawPoolIndex(EventColumnField.TaskCategory, 0)));
        Assert.Equal("<x/>", store.PoolGet(store.RawPoolIndex(EventColumnField.Xml, 0)));
        Assert.Equal("S-1-5-18", store.PoolGet(store.RawPoolIndex(EventColumnField.UserId, 0)));

        long recordId = store.RawRecordId(0, out bool hasRecordId);
        Assert.True(hasRecordId);
        Assert.Equal(99L, recordId);

        Guid activityId = store.RawActivityId(0, out bool hasActivityId);
        Assert.True(hasActivityId);
        Assert.Equal(activity, activityId);

        int processId = store.RawProcessId(0, out bool hasProcessId);
        Assert.True(hasProcessId);
        Assert.Equal(1234, processId);

        int threadId = store.RawThreadId(0, out bool hasThreadId);
        Assert.True(hasThreadId);
        Assert.Equal(5678, threadId);
    }

    [Fact]
    public void Build_SingleEvent_MinEqualsMax()
    {
        long ticks = new DateTime(2023, 3, 3, 3, 3, 3, DateTimeKind.Utc).Ticks;

        EventColumnStore store = EventColumnStore.Build([EventAt(0, ticks)], generation: 0, contentVersion: 0);

        Assert.True(store.TryGetTimeRange(out long minTicks, out long maxTicks));
        Assert.Equal(ticks, minTicks);
        Assert.Equal(ticks, maxTicks);
        Assert.Equal(store.MinTimeTicks, store.MaxTimeTicks);
    }

    [Fact]
    public void Build_UserData_RoundTripsPathValuesTruncatedAndIncomplete()
    {
        ResolvedEvent resolvedEvent = new("live", LogPathType.Channel)
        {
            UserData = ImmutableArray.Create(
                new UserDataField("Result/@value", ImmutableArray.Create("ok", "retry"), IsTruncated: true)),
            UserDataIncomplete = true
        };

        EventColumnStore store = EventColumnStore.Build([resolvedEvent], generation: 1, contentVersion: 1);

        Assert.True(store.RawUserDataIncomplete(0));
        Assert.Equal(1, store.RawUserDataCount(0));
        Assert.Equal("Result/@value", store.PoolGet(store.RawUserDataPathIndex(0, 0)));
        Assert.True(store.RawUserDataTruncated(0, 0));

        ReadOnlySpan<int> values = store.RawUserDataValues(0, 0);
        Assert.Equal(2, values.Length);
        Assert.Equal("ok", store.PoolGet(values[0]));
        Assert.Equal("retry", store.PoolGet(values[1]));
    }

    [Fact]
    public void Build_UserDataMultipleFieldsAndRows_NestsWithoutCrossFieldBleed()
    {
        ResolvedEvent first = new("live", LogPathType.Channel)
        {
            UserData = ImmutableArray.Create(
                new UserDataField("A", ImmutableArray.Create("a0"), IsTruncated: false),
                new UserDataField("B", ImmutableArray.Create("b0", "b1"), IsTruncated: true))
        };
        ResolvedEvent second = new("live", LogPathType.Channel)
        {
            UserData = ImmutableArray.Create(
                new UserDataField("C", ImmutableArray.Create("c0", "c1", "c2"), IsTruncated: false))
        };

        EventColumnStore store = EventColumnStore.Build([first, second], generation: 1, contentVersion: 1);

        Assert.Equal(2, store.RawUserDataCount(0));
        Assert.Equal("B", store.PoolGet(store.RawUserDataPathIndex(0, 1)));
        Assert.True(store.RawUserDataTruncated(0, 1));
        Assert.False(store.RawUserDataTruncated(0, 0));

        ReadOnlySpan<int> bValues = store.RawUserDataValues(0, 1);
        Assert.Equal(2, bValues.Length);
        Assert.Equal("b1", store.PoolGet(bValues[1]));

        Assert.Equal(1, store.RawUserDataCount(1));
        Assert.Equal("C", store.PoolGet(store.RawUserDataPathIndex(1, 0)));

        ReadOnlySpan<int> cValues = store.RawUserDataValues(1, 0);
        Assert.Equal(3, cValues.Length);
        Assert.Equal("c2", store.PoolGet(cValues[2]));
    }

    [Fact]
    public void BuildMultiChunkThenAppend_MinMaxSpanSealedChunksAndPendingTail()
    {
        // Build columnarizes the whole batch into sealed chunks (empty tail); a short Append then stays in the pending
        // tail, so the global min sits in a sealed chunk and the global max in the pending tail.
        const int SealedRows = 4096 * 2; // Two full sealed chunks.
        const int TailRows = 37;         // Below the reseal threshold, so it remains pending.

        long baseTicks = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        ResolvedEvent[] sealedBatch = new ResolvedEvent[SealedRows];
        for (int i = 0; i < SealedRows; i++) { sealedBatch[i] = EventAt(i, baseTicks + 1000 + i); }

        long expectedMin = baseTicks; // Below every other value; placed in the second sealed chunk, not at a physical end.
        sealedBatch[SealedRows - 100] = EventAt(SealedRows - 100, expectedMin);

        ResolvedEvent[] tailBatch = new ResolvedEvent[TailRows];
        for (int i = 0; i < TailRows; i++) { tailBatch[i] = EventAt(SealedRows + i, baseTicks + 2000 + i); }

        long expectedMax = baseTicks + 10_000_000; // Above every other value; placed in the pending tail, not the last row.
        tailBatch[TailRows - 5] = EventAt(SealedRows + TailRows - 5, expectedMax);

        EventColumnStore store = EventColumnStore.Build(sealedBatch, generation: 1, contentVersion: 1).Append(tailBatch);

        Assert.Equal(2, store.SealedChunkCount);
        Assert.Equal(SealedRows, store.SealedCount);
        Assert.Equal(TailRows, store.Count - store.SealedCount);
        Assert.True(store.TryGetTimeRange(out long minTicks, out long maxTicks));
        Assert.Equal(expectedMin, minTicks);
        Assert.Equal(expectedMax, maxTicks);
    }

    [Fact]
    public void ColumnAccessors_PendingRow_Throw()
    {
        EventColumnStore store = EventColumnStore.Build([], generation: 1, contentVersion: 1).Append([Event(0)]);

        Assert.True(store.IsPending(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.RawPoolIndex(EventColumnField.LogName, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.RawEventDataField(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.RawKeywords(0));
    }

    [Fact]
    public void CreateReader_GetDetailAndLean_RoundTripEveryRowAcrossSealedAndPending()
    {
        // Build columnarizes the whole batch into sealed chunks; the short Append leaves a pending tail, so the loop
        // drives both the sealed-row reconstruction path and the pending-row passthrough through the public reader.
        const int SealedRows = 4096 + 10; // Build => two sealed chunks.
        const int TailRows = 15;          // Append keeps these in the pending tail.

        long baseTicks = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        // Row j carries Id j and tick baseTicks + j, so a locator that resolves to the wrong row is caught immediately.
        ResolvedEvent[] sealedBatch = new ResolvedEvent[SealedRows];
        for (int i = 0; i < SealedRows; i++) { sealedBatch[i] = EventAt(i, baseTicks + i); }

        ResolvedEvent[] tailBatch = new ResolvedEvent[TailRows];
        for (int i = 0; i < TailRows; i++) { tailBatch[i] = EventAt(SealedRows + i, baseTicks + SealedRows + i); }

        EventColumnStore store = EventColumnStore.Build(sealedBatch, generation: 3, contentVersion: 9).Append(tailBatch);

        EventLogId logId = new(Guid.NewGuid());
        IEventColumnReader reader = store.CreateReader(logId);

        Assert.Equal(store.Count, reader.Count);
        Assert.Equal(store.Generation, reader.Generation);
        Assert.Equal(logId, reader.LogId);
        Assert.True(store.SealedCount > 0 && store.Count - store.SealedCount == TailRows);

        for (int i = 0; i < store.Count; i++)
        {
            EventLocator locator = reader.LocatorAt(i);

            ResolvedEvent full = reader.GetDetail(locator);
            ResolvedEvent lean = reader.GetDetailLean(locator);

            Assert.Equal(i, full.Id);
            Assert.Equal(baseTicks + i, full.TimeCreated.Ticks);
            Assert.Equal(i, lean.Id);
            Assert.Equal(baseTicks + i, lean.TimeCreated.Ticks);
        }
    }

    [Fact]
    public void Empty_HasNoRowsAndReportsNoTimeRange()
    {
        Assert.Equal(0, EventColumnStore.Empty.Count);
        Assert.False(EventColumnStore.Empty.TryGetTimeRange(out _, out _));
    }

    [Fact]
    public void GetPendingEvent_PendingRow_ReturnsEventAndThrowsForSealed()
    {
        ResolvedEvent pending = new("live", LogPathType.Channel) { LogName = "Security", Keywords = ["Audit Success"] };
        EventColumnStore store = EventColumnStore.Build([Event(0)], generation: 1, contentVersion: 1).Append([pending]);

        Assert.True(store.IsPending(1));
        Assert.Same(pending, store.GetPendingEvent(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetPendingEvent(0));
    }

    [Fact]
    public void TryGetTimeRange_MatchesTimeColumnAcrossSealedAndPendingRows()
    {
        const int SealedRows = 4096 + 250; // Build => two sealed chunks.
        const int TailRows = 300;          // Append keeps 300 rows in the pending tail.

        long baseTicks = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        // A deterministic non-monotonic spread so neither physical end is an extreme.
        ResolvedEvent[] sealedBatch = new ResolvedEvent[SealedRows];
        for (int i = 0; i < SealedRows; i++) { sealedBatch[i] = EventAt(i, baseTicks + ((i * 2654435761L) % 1_000_003L)); }

        ResolvedEvent[] tailBatch = new ResolvedEvent[TailRows];
        for (int i = 0; i < TailRows; i++) { tailBatch[i] = EventAt(SealedRows + i, baseTicks + (((i + 7) * 40503L) % 999_983L)); }

        EventColumnStore store = EventColumnStore.Build(sealedBatch, generation: 1, contentVersion: 1).Append(tailBatch);

        Assert.True(store.SealedCount > 0);
        Assert.True(store.Count - store.SealedCount > 0);

        // Cross-check the O(1) range against a full scan of the store's time column (sealed rows + pending tail).
        long expectedMin = long.MaxValue;
        long expectedMax = long.MinValue;

        for (int i = 0; i < store.Count; i++)
        {
            long ticks = store.RawTimeTicks(i);
            expectedMin = Math.Min(expectedMin, ticks);
            expectedMax = Math.Max(expectedMax, ticks);
        }

        Assert.True(store.TryGetTimeRange(out long minTicks, out long maxTicks));
        Assert.Equal(expectedMin, minTicks);
        Assert.Equal(expectedMax, maxTicks);
    }

    [Fact]
    public void WithReloadGeneration_NewBatch_BumpsGenerationContinuesContentVersion()
    {
        EventColumnStore store = EventColumnStore.Build([Event(0)], generation: 1, contentVersion: 5);
        EventColumnStore appended = store.Append([Event(1)]);

        EventColumnStore reloaded = appended.WithReloadGeneration([Event(2)]);

        Assert.Equal(appended.Generation + 1, reloaded.Generation);
        Assert.True(reloaded.ContentVersion > appended.ContentVersion);
        Assert.Equal(1, reloaded.Count);
    }

    private static void AssertKind(EventColumnStore store, int field, StoredFieldKind expected) =>
        Assert.Equal(expected, store.RawEventDataField(0, field).Kind);

    private static ResolvedEvent Event(int id) => new("live", LogPathType.Channel) { Id = id };

    private static ResolvedEvent EventAt(int id, long ticks) =>
        new("live", LogPathType.Channel) { Id = id, TimeCreated = new DateTime(ticks, DateTimeKind.Utc) };

    private static ResolvedEvent EventWithProperties(params (string Name, EventProperty Value)[] fields)
    {
        string template = "<template>"
            + string.Concat(fields.Select(field => $"<data name=\"{SecurityElement.Escape(field.Name)}\"/>"))
            + "</template>";

        TemplateFieldSchema schema = new TemplateAnalyzer().GetTemplateInfo(template).Schema;

        ImmutableArray<EventProperty>.Builder values = ImmutableArray.CreateBuilder<EventProperty>(fields.Length);

        foreach ((string _, EventProperty value) in fields) { values.Add(value); }

        return new ResolvedEvent("live", LogPathType.Channel)
        {
            EventDataValues = values.MoveToImmutable(),
            EventDataSchema = schema
        };
    }

    private sealed class UnknownReference
    {
        public override string ToString() => "unknown-ref";
    }
}

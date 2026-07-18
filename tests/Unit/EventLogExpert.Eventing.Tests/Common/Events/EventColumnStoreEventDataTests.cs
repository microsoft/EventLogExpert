// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.TestUtils;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventColumnStoreEventDataTests
{
    private const string WuClient = "Microsoft-Windows-WindowsUpdateClient";

    private static readonly EventLogId s_logId = EventLogId.Create();
    private static readonly string[] s_updateProviders = [WuClient, "Microsoft-Windows-Servicing"];
    private static readonly string[] s_errorCodeUserDataPaths = ["CbsPackageChangeState/ErrorCode", "CbsUpdateChangeState/ErrorCode"];

    [Fact]
    public void BucketTimeTicksByEventData_ClassifiesRowsByCodeWithOtherForNonTargets()
    {
        IEventColumnReader reader = ReaderFor(
            Event("LogonType", 3L),
            Event("LogonType", 3L),
            Event("LogonType", 10L),
            Event("LogonType", 7L)); // 7 is not a target -> Other

        long[] targetCodes = [3, 10];
        int slotCount = targetCodes.Length + 1;
        int[] slotCounts = new int[slotCount];
        reader.BucketTimeTicksByEventData(AllSurvive(reader.Count), 0, long.MaxValue, 1, "LogonType", targetCodes, slotCounts, CancellationToken.None);

        Assert.Equal(2, slotCounts[0]); // code 3
        Assert.Equal(1, slotCounts[1]); // code 10
        Assert.Equal(1, slotCounts[2]); // Other (code 7)
    }

    [Fact]
    public void BucketTimeTicksByEventData_IsAllocationFreeOnSealedRows()
    {
        // EventColumnStore.Build seals the whole batch. One schema means the field-index memo resolves once, so the
        // per-row path performs no allocation; only the fixed per-scan memo array (int[schemaCount]) is charged.
        var events = new ResolvedEvent[8192];

        for (int index = 0; index < events.Length; index++)
        {
            events[index] = Event("LogonType", index % 3 == 0 ? 3L : 10L, index);
        }

        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(s_logId);
        int[] rank = AllSurvive(reader.Count);
        long[] targetCodes = [3, 10];
        int[] slotCounts = new int[targetCodes.Length + 1];

        // Warm: first scan resolves the schema and JITs.
        reader.BucketTimeTicksByEventData(rank, 0, long.MaxValue, 1, "LogonType", targetCodes, slotCounts, CancellationToken.None);

        long before = GC.GetAllocatedBytesForCurrentThread();
        reader.BucketTimeTicksByEventData(rank, 0, long.MaxValue, 1, "LogonType", targetCodes, slotCounts, CancellationToken.None);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // A single per-row byte would cost 8192; the fixed memo array is a few dozen bytes, so this proves the hot path is allocation-free.
        Assert.True(delta < 512, $"Per-row allocation detected: {delta} bytes over {events.Length} sealed rows.");
    }

    [Fact]
    public void BucketTimeTicksByEventDataHResult_ChartsServicingUserDataErrorCode_OmitsSuccessEmptyAndNoLeaf()
    {
        IEventColumnReader reader = ReaderFor(
            ServicingEvent("CbsPackageChangeState/ErrorCode", "0x800f0816"),
            ServicingEvent("CbsUpdateChangeState/ErrorCode", "0x800F0922"),
            ServicingEvent("CbsPackageChangeState/ErrorCode", "0x0"),          // success
            ServicingEvent("CbsUpdateChangeState/ErrorCode", ""),               // empty (event 7)
            ServicingEvent("CbsPackageInitiateChanges/Client", "CbsTask"));     // no ErrorCode leaf (event 1)

        long[] targetCodes = [0x800F0816L, 0x800F0922L];
        int[] slotCounts = new int[targetCodes.Length + 1];
        reader.BucketTimeTicksByEventDataHResult(AllSurvive(reader.Count), 0, long.MaxValue, 1, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, targetCodes, slotCounts, CancellationToken.None);

        Assert.Equal(1, slotCounts[0]); // 0x800F0816 (CbsPackageChangeState)
        Assert.Equal(1, slotCounts[1]); // 0x800F0922 (CbsUpdateChangeState)
        Assert.Equal(0, slotCounts[2]); // success, empty, and the no-ErrorCode-leaf row are omitted
    }

    [Fact]
    public void BucketTimeTicksByEventDataHResult_ClassifiesEligibleFailures_OmitsIneligible()
    {
        IEventColumnReader reader = ReaderFor(
            UpdateEvent(WuClient, unchecked((int)0x800F081Fu)),
            UpdateEvent(WuClient, unchecked((int)0x800F0823u)),
            UpdateEvent(WuClient, unchecked((int)0x80070005u)),
            UpdateEvent(WuClient, 0),
            UpdateEvent("Some-Other-Provider", unchecked((int)0x800F081Fu)));

        long[] targetCodes = [0x800F081FL, 0x800F0823L];
        int[] slotCounts = new int[targetCodes.Length + 1];
        reader.BucketTimeTicksByEventDataHResult(AllSurvive(reader.Count), 0, long.MaxValue, 1, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, targetCodes, slotCounts, CancellationToken.None);

        Assert.Equal(1, slotCounts[0]);
        Assert.Equal(1, slotCounts[1]);
        Assert.Equal(1, slotCounts[2]); // Other holds only 0x80070005; the zero and ineligible rows are omitted, not bucketed
    }

    [Fact]
    public void BucketTimeTicksByEventDataHResult_IsAllocationFreeOnSealedRows()
    {
        var events = new ResolvedEvent[8192];

        for (int index = 0; index < events.Length; index++)
        {
            events[index] = UpdateEvent(WuClient, index % 3 == 0 ? unchecked((int)0x800F081Fu) : unchecked((int)0x800F0823u), index);
        }

        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(s_logId);
        int[] rank = AllSurvive(reader.Count);
        long[] targetCodes = [0x800F081FL, 0x800F0823L];
        int[] slotCounts = new int[targetCodes.Length + 1];

        reader.BucketTimeTicksByEventDataHResult(rank, 0, long.MaxValue, 1, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, targetCodes, slotCounts, CancellationToken.None);

        long before = GC.GetAllocatedBytesForCurrentThread();
        reader.BucketTimeTicksByEventDataHResult(rank, 0, long.MaxValue, 1, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, targetCodes, slotCounts, CancellationToken.None);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // The eligible-index buffer is stack-allocated and the schema memo is a fixed per-scan array, so the per-row path is
        // allocation-free; a single per-row byte would cost 8192.
        Assert.True(delta < 512, $"Per-row allocation detected: {delta} bytes over {events.Length} sealed rows.");
    }

    [Fact]
    public void CountEventDataHResults_CaseInsensitiveAllowlist_IsNormalizedToOrdinal()
    {
        // A caller-supplied OrdinalIgnoreCase set must be normalized to ordinal so pending matching stays case-sensitive like
        // the sealed pool lookup; otherwise a case-variant provider would match pending rows but not sealed rows.
        var caseInsensitive = new HashSet<string>([WuClient], StringComparer.OrdinalIgnoreCase);
        ResolvedEvent[] events =
        [
            UpdateEvent(WuClient, unchecked((int)0x800F0823u)),
            UpdateEvent("MICROSOFT-WINDOWS-WINDOWSUPDATECLIENT", unchecked((int)0x800F081Fu))
        ];

        foreach (bool sealRows in new[] { true, false })
        {
            EventColumnStore store = sealRows
                ? EventColumnStore.Build(events, generation: 0, contentVersion: 0)
                : EventColumnStore.Build([], generation: 0, contentVersion: 0).Append(events);
            IEventColumnReader reader = store.CreateReader(s_logId);

            var counts = new Dictionary<long, int>();
            reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", caseInsensitive, s_errorCodeUserDataPaths, counts, CancellationToken.None);

            Assert.Single(counts);
            Assert.Equal(1, counts[0x800F0823L]);
            Assert.DoesNotContain(0x800F081FL, counts.Keys);
        }
    }

    [Fact]
    public void CountEventDataHResults_FoldsHexAndDecimalStringSpellings()
    {
        IEventColumnReader reader = ReaderFor(
            UpdateEvent(WuClient, "0x800F081F"),
            UpdateEvent(WuClient, "2148468767")); // decimal spelling of 0x800F081F

        var counts = new Dictionary<long, int>();
        reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(2, counts[0x800F081FL]);
    }

    [Fact]
    public void CountEventDataHResults_IsAllocationFreeOnPendingRows()
    {
        // Stay below TargetChunkSize so Append leaves every row pending (unsealed), exercising the pending provider-match path.
        var events = new ResolvedEvent[3000];

        for (int index = 0; index < events.Length; index++)
        {
            events[index] = UpdateEvent(WuClient, index % 3 == 0 ? unchecked((int)0x800F081Fu) : unchecked((int)0x800F0823u), index);
        }

        EventColumnStore store = EventColumnStore.Build([], generation: 0, contentVersion: 0).Append(events);
        Assert.Equal(0, store.SealedCount);
        IEventColumnReader reader = store.CreateReader(s_logId);
        int[] rank = AllSurvive(reader.Count);
        var counts = new Dictionary<long, int>();

        reader.CountEventDataHResults(rank, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

        counts.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        reader.CountEventDataHResults(rank, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // The pending provider match uses a scan-local set built once, not a per-row enumerator (which cost 32 bytes x 3000).
        Assert.True(delta < 4096, $"Per-row allocation detected on pending rows: {delta} bytes over {events.Length} rows.");
    }

    [Fact]
    public void CountEventDataHResults_OmitsZeroAbsentAndIneligibleProvider()
    {
        IEventColumnReader reader = ReaderFor(
            UpdateEvent(WuClient, unchecked((int)0x800F0823u)),
            UpdateEvent(WuClient, 0),
            UpdateEventNoData(WuClient),
            UpdateEvent("Some-Other-Provider", unchecked((int)0x800F0823u)));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(1, counts[0x800F0823L]);
    }

    [Fact]
    public void CountEventDataHResults_ReadsSignExtendedNegativeHexInt32()
    {
        IEventColumnReader reader = ReaderFor(UpdateEvent(WuClient, unchecked((int)0x800F0823u)));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(1, counts[0x800F0823L]);
    }

    [Fact]
    public void CountEventDataHResults_ServicingUserData_OmitsNonTargetErrorCodePath()
    {
        // The dimension keys on the two curated Cbs* paths, not any */ErrorCode leaf; an unlisted template is omitted.
        IEventColumnReader reader = ReaderFor(
            ServicingEvent("SomeOtherTemplate/ErrorCode", "0x800f0816"),
            ServicingEvent("CbsPackageChangeState/ErrorCode", "0x800F0922"));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(1, counts[0x800F0922L]);
        Assert.DoesNotContain(0x800F0816L, counts.Keys);
    }

    [Fact]
    public void CountEventDataHResults_ServicingUserData_PathMatchIsOrdinal_SealedAndPendingOmitCaseVariant()
    {
        // Storage keys are canonical, so the sealed pool-index compare is exact; the pending mirror must be ordinal too,
        // or a case-variant path would chart on one store but not the other. Both must omit it.
        foreach (bool sealRows in new[] { true, false })
        {
            ResolvedEvent[] corpus = [ServicingEvent("cbspackagechangestate/errorcode", "0x800f0816")];
            EventColumnStore store = sealRows
                ? EventColumnStore.Build(corpus, generation: 0, contentVersion: 0)
                : EventColumnStore.Build([], generation: 0, contentVersion: 0).Append(corpus);
            IEventColumnReader reader = store.CreateReader(s_logId);

            var counts = new Dictionary<long, int>();
            reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

            Assert.Empty(counts);
        }
    }

    [Fact]
    public void CountEventDataHResults_ServicingUserData_ResolvesTargetPathInternedInLaterChunk()
    {
        // The target UserData path is interned only by the final row, which lands in a later sealed chunk (Build chunks at
        // 4096 rows); resolving it once against the shared store pool must still find it so the multi-chunk scan charts it.
        const int chunkSize = 4096;
        var events = new ResolvedEvent[chunkSize + 1];

        for (int index = 0; index < chunkSize; index++)
        {
            events[index] = UpdateEvent(WuClient, 0, index); // WUClient success rows fill the first chunk (no Servicing path)
        }

        events[chunkSize] = ServicingEvent("CbsPackageChangeState/ErrorCode", "0x800f0816", chunkSize);

        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(s_logId);

        var counts = new Dictionary<long, int>();
        reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(1, counts[0x800F0816L]);
    }

    [Fact]
    public void CountEventDataHResults_ServicingUserData_SealedAndPending_ChartFailuresOmitSuccess()
    {
        foreach (bool sealRows in new[] { true, false })
        {
            ResolvedEvent[] corpus =
            [
                ServicingEvent("CbsPackageChangeState/ErrorCode", "0x800f0816"),
                ServicingEvent("CbsUpdateChangeState/ErrorCode", "0x800F0922", 1),
                ServicingEvent("CbsPackageChangeState/ErrorCode", "0x0", 2)
            ];
            EventColumnStore store = sealRows
                ? EventColumnStore.Build(corpus, generation: 0, contentVersion: 0)
                : EventColumnStore.Build([], generation: 0, contentVersion: 0).Append(corpus);
            IEventColumnReader reader = store.CreateReader(s_logId);

            var counts = new Dictionary<long, int>();
            reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

            Assert.Equal(2, counts.Count);
            Assert.Equal(1, counts[0x800F0816L]);
            Assert.Equal(1, counts[0x800F0922L]);
        }
    }

    [Fact]
    public void CountEventDataValues_FoldsDecimalAndHexSpellingsOfOneCode()
    {
        IEventColumnReader reader = ReaderFor(
            Event("TicketEncryptionType", 23L),
            Event("TicketEncryptionType", "0x17"),
            Event("TicketEncryptionType", 18L));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataValues(AllSurvive(reader.Count), "TicketEncryptionType", counts, CancellationToken.None);

        Assert.Equal(2, counts.Count);
        Assert.Equal(2, counts[23]); // decimal 23 and hex "0x17" fold to the same code
        Assert.Equal(1, counts[18]);
    }

    [Fact]
    public void CountEventDataValues_OmitsRowsThatLackTheField()
    {
        IEventColumnReader reader = ReaderFor(Event("LogonType", 3L), EventWithoutData());

        var counts = new Dictionary<long, int>();
        reader.CountEventDataValues(AllSurvive(reader.Count), "LogonType", counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(1, counts[3]);
    }

    [Fact]
    public void CountEventDataValues_ReadsUnsignedIntegralCodes()
    {
        // A UInt32-typed value exercises the unsigned direct-read branch and must fold with the signed Int64 spelling of the same code.
        IEventColumnReader reader = ReaderFor(Event("LogonType", (uint)3), Event("LogonType", 3L));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataValues(AllSurvive(reader.Count), "LogonType", counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(2, counts[3]);
    }

    [Fact]
    public void CountEventDataValues_RejectsHexCodeThatOverflowsALong()
    {
        // A high-bit hex form must not wrap to a negative code (-1) and fold with a real code.
        IEventColumnReader reader = ReaderFor(Event("TicketEncryptionType", "0xFFFFFFFFFFFFFFFF"), Event("TicketEncryptionType", 23L));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataValues(AllSurvive(reader.Count), "TicketEncryptionType", counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(1, counts[23]);
        Assert.DoesNotContain(-1L, counts.Keys);
    }

    [Fact]
    public void HResultScans_MixedEventDataAndUserData_CountAndBucketAgree()
    {
        // A WUClient EventData errorCode and a Servicing UserData ErrorCode in one store both chart; the EventData-first /
        // UserData-fallback contributes exactly one slot per row, so the bucket sum equals the count total.
        IEventColumnReader reader = ReaderFor(
            UpdateEvent(WuClient, unchecked((int)0x800F081Fu)),
            ServicingEvent("CbsPackageChangeState/ErrorCode", "0x800f0816", 1));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

        Assert.Equal(2, counts.Count);
        Assert.Equal(1, counts[0x800F081FL]);
        Assert.Equal(1, counts[0x800F0816L]);

        long[] targetCodes = [0x800F081FL, 0x800F0816L];
        int[] slotCounts = new int[targetCodes.Length + 1];
        reader.BucketTimeTicksByEventDataHResult(AllSurvive(reader.Count), 0, long.MaxValue, 1, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, targetCodes, slotCounts, CancellationToken.None);

        Assert.Equal(1, slotCounts[0]);
        Assert.Equal(1, slotCounts[1]);
        Assert.Equal(0, slotCounts[2]);
    }

    private static int[] AllSurvive(int count)
    {
        int[] rank = new int[count];

        for (int index = 0; index < count; index++) { rank[index] = index; }

        return rank;
    }

    private static ResolvedEvent Event(string fieldName, object value, int tick = 0) =>
        new ResolvedEvent("TestLog", LogPathType.Channel) { Id = 4624, TimeCreated = new DateTime(tick, DateTimeKind.Utc) }
            .WithEventData((fieldName, value));

    private static ResolvedEvent EventWithoutData() =>
        new("TestLog", LogPathType.Channel) { Id = 4624, TimeCreated = new DateTime(0, DateTimeKind.Utc) };

    private static IEventColumnReader ReaderFor(params ResolvedEvent[] events) =>
        EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(s_logId);

    private static ResolvedEvent ServicingEvent(string userDataPath, string? errorCode, int tick = 0) =>
        new ResolvedEvent("TestLog", LogPathType.Channel) { Id = 3, Source = "Microsoft-Windows-Servicing", TimeCreated = new DateTime(tick, DateTimeKind.Utc) }
            .WithUserData((userDataPath, errorCode));

    private static ResolvedEvent UpdateEvent(string source, object errorCode, int tick = 0) =>
        new ResolvedEvent("TestLog", LogPathType.Channel) { Id = 20, Source = source, TimeCreated = new DateTime(tick, DateTimeKind.Utc) }
            .WithEventData(("errorCode", errorCode));

    private static ResolvedEvent UpdateEventNoData(string source) =>
        new("TestLog", LogPathType.Channel) { Id = 44, Source = source, TimeCreated = new DateTime(0, DateTimeKind.Utc) };
}

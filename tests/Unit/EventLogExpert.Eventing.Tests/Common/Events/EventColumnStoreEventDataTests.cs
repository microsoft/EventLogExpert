// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.TestUtils;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventColumnStoreEventDataTests
{
    private const string WuClient = "Microsoft-Windows-WindowsUpdateClient";
    private static readonly EventLogId s_logId = EventLogId.Create();
    private static readonly string[] s_updateProviders = [WuClient, "Microsoft-Windows-Servicing"];
    private static readonly string[] s_errorCodeUserDataPaths = ["CbsPackageChangeState/ErrorCode", "CbsUpdateChangeState/ErrorCode"];

    [Fact]
    public void BucketTimeTicksByEventDataHResult_ChartsServicingUserDataErrorCode_OmitsSuccessEmptyAndNoLeaf()
    {
        IEventColumnReader reader = ReaderFor(
            ServicingEvent("CbsPackageChangeState/ErrorCode", "0x800f0816"),
            ServicingEvent("CbsUpdateChangeState/ErrorCode", "0x800F0922"),
            ServicingEvent("CbsPackageChangeState/ErrorCode", "0x0"),
            ServicingEvent("CbsUpdateChangeState/ErrorCode", ""),
            ServicingEvent("CbsPackageInitiateChanges/Client", "CbsTask"));

        long[] targetCodes = [0x800F0816L, 0x800F0922L];
        int[] slotCounts = new int[targetCodes.Length + 1];
        reader.BucketTimeTicksByEventDataHResult(AllSurvive(reader.Count), 0, long.MaxValue, 1, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, targetCodes, slotCounts, CancellationToken.None);

        Assert.Equal(1, slotCounts[0]);
        Assert.Equal(1, slotCounts[1]);
        Assert.Equal(0, slotCounts[2]);
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
        Assert.Equal(1, slotCounts[2]);
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

        long delta = long.MaxValue;

        for (int iteration = 0; iteration < 16; iteration++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            reader.BucketTimeTicksByEventDataHResult(rank, 0, long.MaxValue, 1, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, targetCodes, slotCounts, CancellationToken.None);
            delta = Math.Min(delta, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.True(delta < 512, $"Per-row allocation detected: {delta} bytes over {events.Length} sealed rows.");
    }

    [Fact]
    public void BucketTimeTicksByEventDataString_UsesSameCandidateSelectionAsCounts()
    {
        IEventColumnReader reader = ReaderFor(
            ProcessEvent(("NewProcessName", @"C:\Windows\System32\rundll32.exe")),
            ProcessEvent(("NewProcessName", "-"), ("Image", @"C:\temp\evil.exe")),
            ProcessEvent(("NewProcessName", @"C:\tools\rare.exe")),
            ProcessEvent(("NewProcessName", "-")));
        string[] candidateFields = ["NewProcessName", "Image"];

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        reader.CountEventDataStringValues(AllSurvive(reader.Count), candidateFields, counts, CancellationToken.None);

        var rawValueToSlot = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [@"C:\Windows\System32\rundll32.exe"] = 0,
            [@"C:\temp\evil.exe"] = 1
        };
        int[] slotCounts = new int[3];
        reader.BucketTimeTicksByEventDataString(AllSurvive(reader.Count), 0, long.MaxValue, 1, candidateFields, rawValueToSlot, slotCount: 3, slotCounts, CancellationToken.None);

        Assert.Equal(3, counts.Values.Sum());
        Assert.Equal(1, slotCounts[0]);
        Assert.Equal(1, slotCounts[1]);
        Assert.Equal(2, slotCounts[2]);
    }

    [Fact]
    public void BucketTimeTicksByEventData_ClassifiesRowsByCodeWithOtherForNonTargets()
    {
        IEventColumnReader reader = ReaderFor(
            Event("LogonType", 3L),
            Event("LogonType", 3L),
            Event("LogonType", 10L),
            Event("LogonType", 7L));

        long[] targetCodes = [3, 10];
        int slotCount = targetCodes.Length + 1;
        int[] slotCounts = new int[slotCount];
        reader.BucketTimeTicksByEventData(AllSurvive(reader.Count), 0, long.MaxValue, 1, "LogonType", targetCodes, slotCounts, CancellationToken.None);

        Assert.Equal(2, slotCounts[0]);
        Assert.Equal(1, slotCounts[1]);
        Assert.Equal(1, slotCounts[2]);
    }

    [Fact]
    public void BucketTimeTicksByEventData_IsAllocationFreeOnSealedRows()
    {
        var events = new ResolvedEvent[8192];

        for (int index = 0; index < events.Length; index++)
        {
            events[index] = Event("LogonType", index % 3 == 0 ? 3L : 10L, index);
        }

        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(s_logId);
        int[] rank = AllSurvive(reader.Count);
        long[] targetCodes = [3, 10];
        int[] slotCounts = new int[targetCodes.Length + 1];

        reader.BucketTimeTicksByEventData(rank, 0, long.MaxValue, 1, "LogonType", targetCodes, slotCounts, CancellationToken.None);

        long delta = long.MaxValue;

        for (int iteration = 0; iteration < 16; iteration++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            reader.BucketTimeTicksByEventData(rank, 0, long.MaxValue, 1, "LogonType", targetCodes, slotCounts, CancellationToken.None);
            delta = Math.Min(delta, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.True(delta < 512, $"Per-row allocation detected: {delta} bytes over {events.Length} sealed rows.");
    }

    [Fact]
    public void CountEventDataHResults_CaseInsensitiveAllowlist_IsNormalizedToOrdinal()
    {
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
            UpdateEvent(WuClient, "2148468767"));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(2, counts[0x800F081FL]);
    }

    [Fact]
    public void CountEventDataHResults_IsAllocationFreeOnPendingRows()
    {
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

        long delta = long.MaxValue;

        for (int iteration = 0; iteration < 16; iteration++)
        {
            counts.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            reader.CountEventDataHResults(rank, "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);
            delta = Math.Min(delta, GC.GetAllocatedBytesForCurrentThread() - before);
        }

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
        foreach (bool sealRows in new[] { true, false })
        {
            ResolvedEvent[] sample = [ServicingEvent("cbspackagechangestate/errorcode", "0x800f0816")];
            EventColumnStore store = sealRows
                ? EventColumnStore.Build(sample, generation: 0, contentVersion: 0)
                : EventColumnStore.Build([], generation: 0, contentVersion: 0).Append(sample);
            IEventColumnReader reader = store.CreateReader(s_logId);

            var counts = new Dictionary<long, int>();
            reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

            Assert.Empty(counts);
        }
    }

    [Fact]
    public void CountEventDataHResults_ServicingUserData_ResolvesTargetPathInternedInLaterChunk()
    {
        const int chunkSize = 4096;
        var events = new ResolvedEvent[chunkSize + 1];

        for (int index = 0; index < chunkSize; index++)
        {
            events[index] = UpdateEvent(WuClient, 0, index);
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
            ResolvedEvent[] sample =
            [
                ServicingEvent("CbsPackageChangeState/ErrorCode", "0x800f0816"),
                ServicingEvent("CbsUpdateChangeState/ErrorCode", "0x800F0922", 1),
                ServicingEvent("CbsPackageChangeState/ErrorCode", "0x0", 2)
            ];
            EventColumnStore store = sealRows
                ? EventColumnStore.Build(sample, generation: 0, contentVersion: 0)
                : EventColumnStore.Build([], generation: 0, contentVersion: 0).Append(sample);
            IEventColumnReader reader = store.CreateReader(s_logId);

            var counts = new Dictionary<long, int>();
            reader.CountEventDataHResults(AllSurvive(reader.Count), "errorCode", s_updateProviders, s_errorCodeUserDataPaths, counts, CancellationToken.None);

            Assert.Equal(2, counts.Count);
            Assert.Equal(1, counts[0x800F0816L]);
            Assert.Equal(1, counts[0x800F0922L]);
        }
    }

    [Fact]
    public void CountEventDataStringValues_FallsThroughUnusableOrNonStringCandidates()
    {
        IEventColumnReader reader = ReaderFor(
            ProcessEvent(("NewProcessName", "-"), ("Image", @"C:\x\evil.exe")),
            ProcessEvent(("NewProcessName", @"C:\x\good.exe"), ("Image", @"C:\x\ignored.exe")),
            ProcessEvent(("NewProcessName", "  "), ("Image", "-")),
            ProcessEvent(("NewProcessName", 42L), ("Image", @"C:\x\numeric-fallback.exe")));

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        reader.CountEventDataStringValues(AllSurvive(reader.Count), ["NewProcessName", "Image"], counts, CancellationToken.None);

        Assert.Equal(3, counts.Count);
        Assert.Equal(1, counts[@"C:\x\evil.exe"]);
        Assert.Equal(1, counts[@"C:\x\good.exe"]);
        Assert.Equal(1, counts[@"C:\x\numeric-fallback.exe"]);
        Assert.DoesNotContain("-", counts.Keys);
        Assert.DoesNotContain(@"C:\x\ignored.exe", counts.Keys);
    }

    [Fact]
    public void CountEventDataStringValues_NonStringReferenceRejectedConsistently()
    {
        ResolvedEvent schemaCarrier = new ResolvedEvent("TestLog", LogPathType.Channel) { Id = 4688, TimeCreated = new DateTime(0, DateTimeKind.Utc) }
            .WithEventData(("NewProcessName", "placeholder"));
        ResolvedEvent exoticReferenceEvent = schemaCarrier with { EventDataValues = [EventProperty.FromReference(42L)] };

        foreach (bool sealRows in new[] { true, false })
        {
            EventColumnStore store = sealRows
                ? EventColumnStore.Build([exoticReferenceEvent], generation: 0, contentVersion: 0)
                : EventColumnStore.Build([], generation: 0, contentVersion: 0).Append([exoticReferenceEvent]);
            IEventColumnReader reader = store.CreateReader(s_logId);

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            reader.CountEventDataStringValues(AllSurvive(reader.Count), ["NewProcessName", "Image"], counts, CancellationToken.None);

            Assert.Empty(counts);
        }
    }

    [Fact]
    public void CountEventDataStringValues_SealedAndPendingNativeStringsMatch()
    {
        ResolvedEvent[] events = [ProcessEvent(("NewProcessName", @"C:\Windows\System32\cmd.exe"))];

        foreach (bool sealRows in new[] { true, false })
        {
            EventColumnStore store = sealRows
                ? EventColumnStore.Build(events, generation: 0, contentVersion: 0)
                : EventColumnStore.Build([], generation: 0, contentVersion: 0).Append(events);
            IEventColumnReader reader = store.CreateReader(s_logId);

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            reader.CountEventDataStringValues(AllSurvive(reader.Count), ["NewProcessName", "Image"], counts, CancellationToken.None);

            Assert.Single(counts);
            Assert.Equal(1, counts[@"C:\Windows\System32\cmd.exe"]);
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
        Assert.Equal(2, counts[23]);
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
        IEventColumnReader reader = ReaderFor(Event("LogonType", (uint)3), Event("LogonType", 3L));

        var counts = new Dictionary<long, int>();
        reader.CountEventDataValues(AllSurvive(reader.Count), "LogonType", counts, CancellationToken.None);

        Assert.Single(counts);
        Assert.Equal(2, counts[3]);
    }

    [Fact]
    public void CountEventDataValues_RejectsHexCodeThatOverflowsALong()
    {
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

    private static ResolvedEvent ProcessEvent(params (string Name, object? Value)[] fields) =>
        new ResolvedEvent("TestLog", LogPathType.Channel) { Id = 4688, TimeCreated = new DateTime(0, DateTimeKind.Utc) }
            .WithEventData(fields);

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

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Structured;
using System.Collections.Immutable;
using System.Security;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventColumnStoreHighlightTieBucketTests
{
    private static readonly DateTime s_time = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BucketTimeTicksByEventDataHResultWithTie_OmitsSuccessAndReadsUserData()
    {
        IEventColumnReader reader = Reader(
            WithNamedProperties(
                Event(20, "Microsoft-Windows-WindowsUpdateClient"),
                ("errorCode", unchecked((int)0x80070005))),
            WithNamedProperties(
                Event(21, "Microsoft-Windows-WindowsUpdateClient"),
                ("errorCode", 0)),
            Event(22, "Microsoft-Windows-Servicing") with
            {
                UserData = ImmutableArray.Create(
                    new UserDataField("CbsPackageChangeState/ErrorCode", ImmutableArray.Create("0x80070002"), false))
            });
        int[] slotCounts = new int[3];
        uint[] masks = new uint[3];

        reader.BucketTimeTicksByEventDataHResultWithTie(
            AllSurvive(reader.Count),
            [1, 2, 3],
            masks,
            s_time.Ticks,
            TimeSpan.TicksPerMinute,
            1,
            "errorCode",
            ["Microsoft-Windows-WindowsUpdateClient", "Microsoft-Windows-Servicing"],
            ["CbsPackageChangeState/ErrorCode"],
            [0x80070005L, 0x80070002L],
            slotCounts,
            TestContext.Current.CancellationToken);

        Assert.Equal([1, 1, 0], slotCounts);
        Assert.Equal(1u << 1, masks[0]);
        Assert.Equal(1u << 3, masks[1]);
        Assert.Equal(0u, masks[2]);
    }

    [Fact]
    public void BucketTimeTicksByEventDataStringWithTie_FoldsAliasesAndOtherSlot()
    {
        IEventColumnReader reader = Reader(
            WithNamedProperties(Event(20), ("NewProcessName", (EventProperty)@"C:\Windows\System32\cmd.exe")),
            WithNamedProperties(Event(21), ("Image", (EventProperty)@"C:\Windows\System32\notepad.exe")));
        int[] slotCounts = new int[2];
        uint[] masks = new uint[2];

        reader.BucketTimeTicksByEventDataStringWithTie(
            AllSurvive(reader.Count),
            [1, 2],
            masks,
            s_time.Ticks,
            TimeSpan.TicksPerMinute,
            1,
            ["NewProcessName", "Image"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [@"C:\Windows\System32\cmd.exe"] = 0 },
            2,
            slotCounts,
            TestContext.Current.CancellationToken);

        Assert.Equal([1, 1], slotCounts);
        Assert.Equal(1u << 1, masks[0]);
        Assert.Equal(1u << 2, masks[1]);
    }

    [Fact]
    public void BucketTimeTicksByEventDataWithTie_SetsMaskForCountedRows()
    {
        IEventColumnReader reader = Reader(
            WithNamedProperties(Event(20), ("LogonType", 2)),
            Event(21));
        int[] slotCounts = new int[2];
        uint[] masks = new uint[2];

        reader.BucketTimeTicksByEventDataWithTie(
            AllSurvive(reader.Count),
            [1, 2],
            masks,
            s_time.Ticks,
            TimeSpan.TicksPerMinute,
            1,
            "LogonType",
            [2],
            slotCounts,
            TestContext.Current.CancellationToken);

        Assert.Equal([1, 1], slotCounts);
        Assert.Equal(1u << 1, masks[0]);
        Assert.Equal(1u << 2, masks[1]);
    }

    [Fact]
    public void BucketTimeTicksByFieldWithTie_SetsMaskForLogAndSource()
    {
        IEventColumnReader reader = Reader(
            Event(20, source: "Alpha", owningLog: @"C:\Logs\a.evtx"),
            Event(21, source: "Beta", owningLog: @"C:\Logs\b.evtx"));
        int[] sourceSlotCounts = new int[2];
        uint[] sourceMasks = new uint[2];
        int[] logSlotCounts = new int[2];
        uint[] logMasks = new uint[2];

        reader.BucketTimeTicksByFieldWithTie(
            AllSurvive(reader.Count),
            [1, 2],
            sourceMasks,
            s_time.Ticks,
            TimeSpan.TicksPerMinute,
            1,
            EventFieldId.Source,
            ["Alpha"],
            sourceSlotCounts,
            TestContext.Current.CancellationToken);
        reader.BucketTimeTicksByFieldWithTie(
            AllSurvive(reader.Count),
            [1, 2],
            logMasks,
            s_time.Ticks,
            TimeSpan.TicksPerMinute,
            1,
            EventFieldId.OwningLog,
            [@"C:\Logs\a.evtx"],
            logSlotCounts,
            TestContext.Current.CancellationToken);

        Assert.Equal([1, 1], sourceSlotCounts);
        Assert.Equal([1, 1], logSlotCounts);
        Assert.Equal(1u << 1, sourceMasks[0]);
        Assert.Equal(1u << 2, sourceMasks[1]);
        Assert.Equal(1u << 1, logMasks[0]);
        Assert.Equal(1u << 2, logMasks[1]);
    }

    private static int[] AllSurvive(int count) => Enumerable.Range(0, count).ToArray();

    private static ResolvedEvent Event(
        int id,
        string source = "TestSource",
        string owningLog = "TestLog") =>
        new(owningLog, LogPathType.Channel)
        {
            Id = id,
            Source = source,
            TimeCreated = s_time,
            Level = "Information"
        };

    private static IEventColumnReader Reader(params ResolvedEvent[] events) =>
        EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(EventLogId.Create());

    private static ResolvedEvent WithNamedProperties(
        ResolvedEvent source,
        params (string Name, EventProperty Value)[] fields)
    {
        string template = "<template>"
            + string.Concat(fields.Select(field => $"<data name=\"{SecurityElement.Escape(field.Name)}\"/>"))
            + "</template>";
        TemplateFieldSchema schema = new TemplateAnalyzer().GetTemplateInfo(template).Schema;
        ImmutableArray<EventProperty>.Builder values = ImmutableArray.CreateBuilder<EventProperty>(fields.Length);

        foreach ((string _, EventProperty value) in fields) { values.Add(value); }

        return source with { EventDataValues = values.MoveToImmutable(), EventDataSchema = schema };
    }
}

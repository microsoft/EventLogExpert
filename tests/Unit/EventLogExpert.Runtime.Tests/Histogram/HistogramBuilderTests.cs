// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramBuilderTests
{
    private static readonly EventLogId s_logId = EventLogId.Create();

    [Fact]
    public void Build_AllEventsAtSameTime_ProducesASingleBucket()
    {
        var view = DisplayViewTestFactory.Build(s_logId, EventsAt(500, 500, 500));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Severity, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(1, data.BinCount);
        Assert.Equal(LevelSeverity.SlotCount, data.SlotCount);
        Assert.Equal(LevelSeverity.SlotCount, data.SlotCounts.Length);
        Assert.Equal(3, data.SlotCounts[0]);
        Assert.Equal(3, data.Total);
        Assert.Equal(1, data.BucketSpanTicks);
        Assert.Equal(data.MinUtc, data.MaxUtc);
        Assert.Same(HistogramGroups.Severity, data.Groups);
    }

    [Fact]
    public void Build_CapsBucketCountAtMaxBuckets()
    {
        var view = DisplayViewTestFactory.Build(s_logId, EventsAt(0, 1_000_000));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Severity, maxBuckets: 8, CancellationToken.None);

        Assert.NotNull(data);
        Assert.True(data!.BinCount <= 8);
        Assert.Equal(data.BinCount * LevelSeverity.SlotCount, data.SlotCounts.Length);
    }

    [Fact]
    public void Build_CombinedView_GroupBySource_SumsByLogicalValueAcrossStores()
    {
        // "shared" appears in both child stores (each with its own string pool); the top-N resolution is by logical value,
        // so the combined category count sums both stores rather than mis-classifying by a per-store pool index.
        var first = DisplayViewTestFactory.Build(EventLogId.Create(), SourceEvents(("shared", 2), ("only-a", 1)));
        var second = DisplayViewTestFactory.Build(EventLogId.Create(), SourceEvents(("shared", 3), ("only-b", 1)));
        var combined = new CombinedColumnView([first, second], first.Context);

        HistogramData? data = HistogramBuilder.Build(combined, HistogramDimension.Source, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("shared", data!.Groups[0].Label); // most frequent across both stores (5)
        Assert.Equal(5, GroupTotal(data, 0));
        Assert.Equal(7, data.Total);
    }

    [Fact]
    public void Build_CombinedView_SumsCountsAndSpansAllChildViews()
    {
        var first = DisplayViewTestFactory.Build(EventLogId.Create(), EventsAt(0, 100));
        var second = DisplayViewTestFactory.Build(EventLogId.Create(), EventsAt(200, 300));
        var combined = new CombinedColumnView([first, second], first.Context);

        HistogramData? data = HistogramBuilder.Build(combined, HistogramDimension.Severity, maxBuckets: 10, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(4, data!.Total);
        Assert.Equal(new DateTime(0, DateTimeKind.Utc), data.MinUtc);
        Assert.Equal(new DateTime(300, DateTimeKind.Utc), data.MaxUtc);
    }

    [Fact]
    public void Build_EmptyView_ReturnsNull()
    {
        var view = DisplayViewTestFactory.Build(s_logId, []);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Severity, maxBuckets: 100, CancellationToken.None);

        Assert.Null(data);
    }

    [Fact]
    public void Build_FollowLatest_BucketsEverySurvivorOverTheSurvivorSpan()
    {
        var view = DisplayViewTestFactory.Build(s_logId, EventsAt(0, 100, 200, 300));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Severity, maxBuckets: 4, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(4, data!.Total);
        Assert.Equal(4, Sum(data.SlotCounts));
        Assert.Equal(new DateTime(0, DateTimeKind.Utc), data.MinUtc);
        Assert.Equal(new DateTime(300, DateTimeKind.Utc), data.MaxUtc);
        Assert.True(data.BucketSpanTicks >= 1);
    }

    [Fact]
    public void Build_GroupByErrorCode_ChartsServicingUserDataErrorCode_OmitsSuccess()
    {
        // Servicing failures store their HRESULT in a UserData Cbs*/ErrorCode leaf (not EventData); the dimension must
        // chart them end-to-end alongside a WUClient EventData failure, and omit a servicing success (0x0).
        var events = new List<ResolvedEvent>(ErrorCodeEvents(("Microsoft-Windows-WindowsUpdateClient", unchecked((int)0x800F081Fu), 2)))
        {
            ServicingUserDataEvent("CbsPackageChangeState/ErrorCode", "0x800F0823", 10),
            ServicingUserDataEvent("CbsPackageChangeState/ErrorCode", "0x0", 11)
        };

        var view = DisplayViewTestFactory.Build(s_logId, [.. events]);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ErrorCode, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.False(data!.GroupingFieldAbsent);
        Assert.Equal(3, data.Total); // 2 WUClient 0x800F081F + 1 servicing 0x800F0823; the 0x0 success is omitted
        Assert.Contains(data.Groups, group => group.Label == "0x800F0823 CBS_E_NEW_SERVICING_STACK_REQUIRED");
    }

    [Fact]
    public void Build_GroupByErrorCode_NoFailures_SignalsEmptyStateWithZeroTotalAndNoun()
    {
        var view = DisplayViewTestFactory.Build(s_logId, ErrorCodeEvents(
            ("Microsoft-Windows-WindowsUpdateClient", 0, 4)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ErrorCode, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.True(data!.GroupingFieldAbsent);
        Assert.Empty(data.Groups);
        Assert.Equal(0, data.Total); // the failure-subset count, not view.Count, so the region label never overstates
        Assert.Equal("error-code events", data.EventNoun);
    }

    [Fact]
    public void Build_GroupByErrorCode_OmitsSuccessesAndIneligibleProviders()
    {
        var view = DisplayViewTestFactory.Build(s_logId, ErrorCodeEvents(
            ("Microsoft-Windows-WindowsUpdateClient", unchecked((int)0x800F0823u), 2),
            ("Microsoft-Windows-WindowsUpdateClient", 0, 5),
            ("Some-Other-Provider", unchecked((int)0x800F0823u), 3)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ErrorCode, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(2, data!.Total); // only the eligible failures contribute; successes and other providers are omitted
        Assert.Equal(2, GroupTotal(data, 0));
    }

    [Fact]
    public void Build_GroupByErrorCode_SplitsByHexLabelWithCuratedSymbol()
    {
        var view = DisplayViewTestFactory.Build(s_logId, ErrorCodeEvents(
            ("Microsoft-Windows-WindowsUpdateClient", unchecked((int)0x800F081Fu), 2),
            ("Microsoft-Windows-Servicing", "0x800F0823", 1)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ErrorCode, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.False(data!.GroupingFieldAbsent);
        Assert.Equal("error-code events", data.EventNoun);
        Assert.Equal("0x800F081F CBS_E_SOURCE_MISSING", data.Groups[0].Label);
        Assert.Equal("cat:2148468767", data.Groups[0].Key);
        Assert.Equal(2, GroupTotal(data, 0));
        Assert.Equal("0x800F0823 CBS_E_NEW_SERVICING_STACK_REQUIRED", data.Groups[1].Label);
        Assert.Equal(1, GroupTotal(data, 1));
        Assert.Equal(3, data.Total);
    }

    [Fact]
    public void Build_GroupByErrorCode_UnrecognizedCode_UsesHexLabelWithoutSymbol()
    {
        var view = DisplayViewTestFactory.Build(s_logId, ErrorCodeEvents(
            ("Microsoft-Windows-WindowsUpdateClient", unchecked((int)0x80070005u), 1)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ErrorCode, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("0x80070005", data!.Groups[0].Label);
    }

    [Fact]
    public void Build_GroupByEventId_LabelsCategoriesWithTheNumericIds()
    {
        var events = IdEvents((1000, 3), (2000, 2), (3000, 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.EventId, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("1000", data!.Groups[0].Label);
        Assert.Equal("2000", data.Groups[1].Label);
        Assert.Equal("3000", data.Groups[2].Label);
        Assert.Equal(3, GroupTotal(data, 0));
        Assert.Equal(6, data.Total);
    }

    [Fact]
    public void Build_GroupByLog_EscalatesToFullPathWhenFileNameAndParentBothCollide()
    {
        var events = LogEvents(
            (@"C:\logs\Security.evtx", 2),
            (@"D:\logs\Security.evtx", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Log, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        // Same file name AND parent folder ("logs"), so the parenthetical form also collides and both escalate to the full owning-log path.
        Assert.Equal(@"C:\logs\Security.evtx", data!.Groups[0].Label);
        Assert.Equal(@"D:\logs\Security.evtx", data.Groups[1].Label);
        Assert.NotEqual(data.Groups[0].Label, data.Groups[1].Label);
    }

    [Fact]
    public void Build_GroupByLog_LabelsWithShortNameAndKeepsSameShortNameLogsSeparate()
    {
        var events = LogEvents(
            (@"C:\logsA\Security.evtx", 3),
            (@"C:\logsB\Security.evtx", 2),
            ("System", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Log, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(3, data!.Groups.Count);
        Assert.Equal("Security.evtx (logsA)", data.Groups[0].Label); // colliding file names disambiguated by parent folder...
        Assert.Equal("Security.evtx (logsB)", data.Groups[1].Label);
        Assert.Equal("System", data.Groups[2].Label);
        Assert.Equal(@"cat:C:\logsA\Security.evtx", data.Groups[0].Key); // ...while the toggle Key stays the raw owning-log path
        Assert.NotEqual(data.Groups[0].Key, data.Groups[1].Key);
        Assert.Equal(3, GroupTotal(data, 0));
        Assert.Equal(2, GroupTotal(data, 1));
        Assert.Equal(1, GroupTotal(data, 2));
        Assert.Equal(6, data.Total);
    }

    [Fact]
    public void Build_GroupByLogonType_FieldAbsentFromView_SignalsEmptyState()
    {
        var view = DisplayViewTestFactory.Build(s_logId, EventsAt(0, 100, 200)); // no EventData at all

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.LogonType, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.True(data!.GroupingFieldAbsent);
        Assert.Empty(data.Groups);
        Assert.Equal(3, data.Total); // the true survivor count, so the accessible region label isn't "0 events"
    }

    [Fact]
    public void Build_GroupByLogonType_RowsWithoutTheFieldFallToOther()
    {
        var view = DisplayViewTestFactory.Build(s_logId, EventDataEvents("LogonType", (3, 2), (null, 3)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.LogonType, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(3, GroupTotal(data, 0)); // Other absorbs the 3 field-less rows
        Assert.Equal("Network", data!.Groups[1].Label);
        Assert.Equal(2, GroupTotal(data, 1));
    }

    [Fact]
    public void Build_GroupByLogonType_SplitsByDecodedLabel()
    {
        // LogonType 3 = Network (x3), 10 = RemoteInteractive (x2).
        var view = DisplayViewTestFactory.Build(s_logId, EventDataEvents("LogonType", (3, 3), (10, 2)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.LogonType, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.False(data!.GroupingFieldAbsent);
        Assert.Equal("Network", data.Groups[0].Label);
        Assert.Equal("cat:3", data.Groups[0].Key);
        Assert.Equal(3, GroupTotal(data, 0));
        Assert.Equal("RemoteInteractive", data.Groups[1].Label);
        Assert.Equal(2, GroupTotal(data, 1));
    }

    [Fact]
    public void Build_GroupByLogonType_UnrecognizedCode_KeepsTheRawCodeAsLabel()
    {
        var view = DisplayViewTestFactory.Build(s_logId, EventDataEvents("LogonType", (99, 2)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.LogonType, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("99", data!.Groups[0].Label);
        Assert.Equal("cat:99", data.Groups[0].Key);
    }

    [Fact]
    public void Build_GroupByParentProcessImage_UsesParentImageCandidates()
    {
        var view = DisplayViewTestFactory.Build(
            s_logId,
            [
                ProcessImageEvent(0, ("ParentProcessName", "-"), ("ParentImage", @"C:\Office\WINWORD.EXE")),
                ProcessImageEvent(1, ("ParentProcessName", @"C:\Office\EXCEL.EXE"), ("ParentImage", @"C:\ignored.exe"))
            ]);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ParentProcessImage, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("excel.exe", data.Groups[0].Label);
        Assert.Equal("cat:excel.exe", data.Groups[0].Key);
        Assert.Equal("winword.exe", data.Groups[1].Label);
        Assert.Equal(2, data.Total);
    }

    [Fact]
    public void Build_GroupByProcessImage_FieldAbsentFromView_SignalsEmptyState()
    {
        var view = DisplayViewTestFactory.Build(
            s_logId,
            [
                ProcessImageEvent(0, ("NewProcessName", "-"), ("Image", " ")),
                ProcessImageEvent(1, ("NewProcessName", "  "))
            ]);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ProcessImage, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.True(data.GroupingFieldAbsent);
        Assert.Empty(data.Groups);
        Assert.Equal(2, data.Total);
    }

    [Fact]
    public void Build_GroupByProcessImage_FoldsRawPathsByShortName()
    {
        var view = DisplayViewTestFactory.Build(
            s_logId,
            [
                ProcessImageEvent(0, ("NewProcessName", @"C:\Windows\System32\RUNDLL32.EXE")),
                ProcessImageEvent(1, ("NewProcessName", @"C:\temp\rundll32.exe"))
            ]);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ProcessImage, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Single(data.Groups);
        Assert.Equal("rundll32.exe", data.Groups[0].Label);
        Assert.Equal("cat:rundll32.exe", data.Groups[0].Key);
        Assert.Equal(2, GroupTotal(data, 0));
    }

    [Fact]
    public void Build_GroupByProcessImage_HandlesSlashesQuotesAndTrailingSeparators()
    {
        var view = DisplayViewTestFactory.Build(
            s_logId,
            [
                ProcessImageEvent(0, ("NewProcessName", "/opt/tools/CMD.EXE")),
                ProcessImageEvent(1, ("NewProcessName", "\"C:\\Program Files\\PowerShell\\PowerShell.EXE\"")),
                ProcessImageEvent(2, ("NewProcessName", @"C:\"))
            ]);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ProcessImage, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Contains(data.Groups, group => group.Label == "cmd.exe");
        Assert.Contains(data.Groups, group => group.Label == "powershell.exe");
        Assert.Equal(1, GroupTotal(data, 0));
        Assert.Equal(3, data.Total);
    }

    [Fact]
    public void Build_GroupByProcessImage_RoutesToNewProcessNameThenImage()
    {
        var view = DisplayViewTestFactory.Build(
            s_logId,
            [
                ProcessImageEvent(0, ("NewProcessName", "-"), ("Image", @"C:\Windows\System32\NOTEPAD.EXE"))
            ]);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ProcessImage, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.False(data.GroupingFieldAbsent);
        Assert.Equal("notepad.exe", data.Groups[0].Label);
        Assert.Equal(1, data.Total);
    }

    [Fact]
    public void Build_GroupByProcessImage_TopNamesKeepOtherBand()
    {
        var view = DisplayViewTestFactory.Build(
            s_logId,
            ProcessImageEvents(
                (@"C:\a\alpha.exe", 5),
                (@"C:\b\bravo.exe", 4),
                (@"C:\c\charlie.exe", 3),
                (@"C:\d\delta.exe", 2),
                (@"C:\e\echo.exe", 1),
                (@"C:\f\foxtrot.exe", 1),
                (@"C:\g\golf.exe", 1),
                (@"C:\h\hotel.exe", 1),
                (@"C:\i\india.exe", 1)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ProcessImage, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(HistogramConstants.MaxGroupByCategories + 1, data.Groups.Count);
        Assert.Equal("Other (1 process)", data.Groups[0].Label);
        Assert.DoesNotContain(data.Groups, group => group.Label == "india.exe");
        Assert.Equal(1, GroupTotal(data, 0));
        Assert.Equal(19, data.Total);
    }

    [Fact]
    public void Build_GroupByProcessImage_WhenAllProcessesShownAndRowsAreMissing_LabelsOtherPlainly()
    {
        var view = DisplayViewTestFactory.Build(
            s_logId,
            [
                ProcessImageEvent(0, ("NewProcessName", @"C:\tools\cmd.exe")),
                EventAt(1, nameof(SeverityLevel.Information))
            ]);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.ProcessImage, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("Other", data!.Groups[0].Label);
        Assert.Equal("cmd.exe", data.Groups[1].Label);
        Assert.Equal(1, GroupTotal(data, 0));
        Assert.Equal(1, GroupTotal(data, 1));
        Assert.Equal(2, data.Total);
    }

    [Fact]
    public void Build_GroupBySource_FoldsValuesBeyondTheTopCapIntoOther()
    {
        var events = SourceEvents(
            ("s01", 9),
            ("s02", 8),
            ("s03", 7),
            ("s04", 6),
            ("s05", 5),
            ("s06", 4),
            ("s07", 3),
            ("s08", 2),
            ("s09", 1),
            ("s10", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Source, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(HistogramConstants.MaxGroupByCategories + 1, data!.Groups.Count);
        Assert.Equal("Other (2 sources)", data.Groups[0].Label);
        Assert.DoesNotContain(data.Groups, group => group.Label is "s09" or "s10");
        Assert.Equal(2, GroupTotal(data, 0));
        Assert.Equal(46, data.Total);
    }

    [Fact]
    public void Build_GroupBySource_RanksTopCategoriesByCountDescending()
    {
        var events = SourceEvents(("apache", 3), ("nginx", 2), ("caddy", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Source, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(3, data!.Groups.Count);
        Assert.Equal("apache", data.Groups[0].Label);
        Assert.Equal("nginx", data.Groups[1].Label);
        Assert.Equal("caddy", data.Groups[2].Label);
        Assert.Equal(3, GroupTotal(data, 0));
        Assert.Equal(2, GroupTotal(data, 1));
        Assert.Equal(1, GroupTotal(data, 2));
        Assert.Equal(6, data.Total);
    }

    [Fact]
    public void Build_GroupBySource_WhenEightDistinctValues_EmitsNoOtherGroup()
    {
        var events = SourceEvents(("s01", 8), ("s02", 7), ("s03", 6), ("s04", 5), ("s05", 4), ("s06", 3), ("s07", 2), ("s08", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Source, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(8, data!.Groups.Count);
        Assert.DoesNotContain(data.Groups, group => group.Key == "cat-other");
        Assert.Equal(9, data.SlotCount);
    }

    [Fact]
    public void Build_GroupBySource_WhenNineDistinctValuesAndTieInactive_KeepsTopEightWithOther()
    {
        var events = SourceEvents(("s01", 9), ("s02", 8), ("s03", 7), ("s04", 6), ("s05", 5), ("s06", 4), ("s07", 3), ("s08", 2), ("s09", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Source, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(HistogramConstants.MaxGroupByCategories + 1, data!.Groups.Count);
        Assert.Equal("Other (1 source)", data.Groups[0].Label);
        Assert.DoesNotContain(data.Groups, group => group.Label == "s09");
        Assert.Equal(1, GroupTotal(data, 0));
    }

    [Fact]
    public void Build_GroupBySource_WhenThirteenDistinctValues_KeepsTopEightWithOther()
    {
        var events = SourceEvents(
            ("s01", 13),
            ("s02", 12),
            ("s03", 11),
            ("s04", 10),
            ("s05", 9),
            ("s06", 8),
            ("s07", 7),
            ("s08", 6),
            ("s09", 5),
            ("s10", 4),
            ("s11", 3),
            ("s12", 2),
            ("s13", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Source, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(HistogramConstants.MaxGroupByCategories + 1, data!.Groups.Count);
        Assert.Equal("Other (5 sources)", data.Groups[0].Label);
        Assert.Equal(15, GroupTotal(data, 0));
    }

    [Fact]
    public void Build_GroupBySource_WhenTwelveDistinctValuesAndTieInactive_KeepsTopEightWithOther()
    {
        var events = SourceEvents(
            ("s01", 12),
            ("s02", 11),
            ("s03", 10),
            ("s04", 9),
            ("s05", 8),
            ("s06", 7),
            ("s07", 6),
            ("s08", 5),
            ("s09", 4),
            ("s10", 3),
            ("s11", 2),
            ("s12", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Source, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(HistogramConstants.MaxGroupByCategories + 1, data!.Groups.Count);
        Assert.Equal("Other (4 sources)", data.Groups[0].Label);
        Assert.DoesNotContain(data.Groups, group => group.Label is "s09" or "s10" or "s11" or "s12");
        Assert.Equal(10, GroupTotal(data, 0));
    }

    [Fact]
    public void Build_GroupByTicketEncryptionType_DecimalAndHexCodesFoldIntoOneBand()
    {
        // 0x17 and 23 are the same RC4 etype; the numeric-code scan must fold both spellings into one group.
        var view = DisplayViewTestFactory.Build(s_logId, EventDataEvents("TicketEncryptionType", (23, 2), ("0x17", 1)));

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.TicketEncryptionType, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Single(data!.Groups);
        Assert.Equal("RC4", data.Groups[0].Label);
        Assert.Equal("cat:23", data.Groups[0].Key);
        Assert.Equal(3, GroupTotal(data, 0));
    }

    [Fact]
    public void Build_RecordsPerSeverityCountsInTheBaseBuffer()
    {
        var events = new[]
        {
            EventAt(0, nameof(SeverityLevel.Critical)),
            EventAt(0, nameof(SeverityLevel.Error)),
            EventAt(0, nameof(SeverityLevel.Warning)),
            EventAt(0, nameof(SeverityLevel.Information))
        };
        var view = DisplayViewTestFactory.Build(s_logId, events);

        HistogramData? data = HistogramBuilder.Build(view, HistogramDimension.Severity, maxBuckets: 100, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(1, data!.BinCount);
        Assert.Equal(1, data.SlotCounts[(int)SeverityLevel.Critical]);
        Assert.Equal(1, data.SlotCounts[(int)SeverityLevel.Error]);
        Assert.Equal(1, data.SlotCounts[(int)SeverityLevel.Warning]);
        Assert.Equal(1, data.SlotCounts[(int)SeverityLevel.Information]);
        Assert.Equal(4, data.Total);
    }

    [Fact]
    public void BuildWithHighlightTie_GroupBySource_WhenNineDistinctValues_EmitsNoOtherGroup()
    {
        var events = SourceEvents(("s01", 9), ("s02", 8), ("s03", 7), ("s04", 6), ("s05", 5), ("s06", 4), ("s07", 3), ("s08", 2), ("s09", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);
        SavedFilter filter = CreateFilter("Id == 0");
        byte[] highlightWinners = view.EnsureHighlightWinners([filter], planKey: 1, CancellationToken.None);

        HistogramData? data = HistogramBuilder.BuildWithHighlightTie(view, HistogramDimension.Source, maxBuckets: 100, highlightWinners, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(9, data!.Groups.Count);
        Assert.DoesNotContain(data.Groups, group => group.Key == "cat-other");
        Assert.Equal(10, data.SlotCount);
    }

    [Fact]
    public void BuildWithHighlightTie_GroupBySource_WhenThirteenDistinctValues_KeepsTopEightWithOther()
    {
        var events = SourceEvents(
            ("s01", 13),
            ("s02", 12),
            ("s03", 11),
            ("s04", 10),
            ("s05", 9),
            ("s06", 8),
            ("s07", 7),
            ("s08", 6),
            ("s09", 5),
            ("s10", 4),
            ("s11", 3),
            ("s12", 2),
            ("s13", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);
        SavedFilter filter = CreateFilter("Id == 0");
        byte[] highlightWinners = view.EnsureHighlightWinners([filter], planKey: 1, CancellationToken.None);

        HistogramData? data = HistogramBuilder.BuildWithHighlightTie(view, HistogramDimension.Source, maxBuckets: 100, highlightWinners, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(HistogramConstants.MaxGroupByCategories + 1, data!.Groups.Count);
        Assert.Equal("Other (5 sources)", data.Groups[0].Label);
        Assert.Equal(15, GroupTotal(data, 0));
    }

    [Fact]
    public void BuildWithHighlightTie_GroupBySource_WhenTwelveDistinctValues_EmitsNoOtherGroup()
    {
        var events = SourceEvents(
            ("s01", 12),
            ("s02", 11),
            ("s03", 10),
            ("s04", 9),
            ("s05", 8),
            ("s06", 7),
            ("s07", 6),
            ("s08", 5),
            ("s09", 4),
            ("s10", 3),
            ("s11", 2),
            ("s12", 1));
        var view = DisplayViewTestFactory.Build(s_logId, events);
        SavedFilter filter = CreateFilter("Id == 0");
        byte[] highlightWinners = view.EnsureHighlightWinners([filter], planKey: 1, CancellationToken.None);

        HistogramData? data = HistogramBuilder.BuildWithHighlightTie(view, HistogramDimension.Source, maxBuckets: 100, highlightWinners, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal(HistogramConstants.GraceGroupByCategories, data!.Groups.Count);
        Assert.DoesNotContain(data.Groups, group => group.Key == "cat-other");
        Assert.Equal(13, data.SlotCount);
    }

    private static SavedFilter CreateFilter(string text) =>
        SavedFilter.TryCreate(text, color: HighlightColor.LightRed, isEnabled: true)
        ?? throw new InvalidOperationException($"Failed to compile test filter '{text}'.");

    private static ResolvedEvent[] ErrorCodeEvents(params (string Source, object? ErrorCode, int Count)[] groups)
    {
        var events = new List<ResolvedEvent>();
        long ticks = 0;

        foreach ((string source, object? errorCode, int count) in groups)
        {
            for (int index = 0; index < count; index++)
            {
                var @event = new ResolvedEvent("TestLog", LogPathType.Channel)
                {
                    Id = 20,
                    TimeCreated = new DateTime(ticks++, DateTimeKind.Utc),
                    Source = source
                };

                events.Add(errorCode is null ? @event : @event.WithEventData(("errorCode", errorCode)));
            }
        }

        return [.. events];
    }

    private static ResolvedEvent EventAt(long ticks, string level) =>
        new("TestLog", LogPathType.Channel)
        {
            Id = 0,
            TimeCreated = new DateTime(ticks, DateTimeKind.Utc),
            Level = level
        };

    private static ResolvedEvent[] EventDataEvents(string fieldName, params (object? Value, int Count)[] groups)
    {
        var events = new List<ResolvedEvent>();
        long ticks = 0;

        foreach ((object? value, int count) in groups)
        {
            for (int index = 0; index < count; index++)
            {
                var @event = new ResolvedEvent("TestLog", LogPathType.Channel)
                {
                    Id = 4624,
                    TimeCreated = new DateTime(ticks++, DateTimeKind.Utc)
                };

                events.Add(value is null ? @event : @event.WithEventData((fieldName, value)));
            }
        }

        return [.. events];
    }

    private static ResolvedEvent[] EventsAt(params long[] ticks)
    {
        var events = new ResolvedEvent[ticks.Length];

        for (int index = 0; index < ticks.Length; index++)
        {
            events[index] = new ResolvedEvent("TestLog", LogPathType.Channel)
            {
                Id = index,
                TimeCreated = new DateTime(ticks[index], DateTimeKind.Utc)
            };
        }

        return events;
    }

    private static int GroupTotal(HistogramData data, int groupIndex)
    {
        int total = 0;

        for (int bin = 0; bin < data.BinCount; bin++)
        {
            int offset = bin * data.SlotCount;

            foreach (int slot in data.Groups[groupIndex].SlotIndices) { total += data.SlotCounts[offset + slot]; }
        }

        return total;
    }

    private static ResolvedEvent[] IdEvents(params (int Id, int Count)[] groups)
    {
        var events = new List<ResolvedEvent>();
        long ticks = 0;

        foreach ((int id, int count) in groups)
        {
            for (int index = 0; index < count; index++)
            {
                events.Add(new ResolvedEvent("TestLog", LogPathType.Channel)
                {
                    Id = id,
                    TimeCreated = new DateTime(ticks++, DateTimeKind.Utc)
                });
            }
        }

        return [.. events];
    }

    private static ResolvedEvent[] LogEvents(params (string OwningLog, int Count)[] groups)
    {
        var events = new List<ResolvedEvent>();
        long ticks = 0;

        foreach ((string owningLog, int count) in groups)
        {
            for (int index = 0; index < count; index++)
            {
                events.Add(new ResolvedEvent(owningLog, LogPathType.Channel)
                {
                    Id = 0,
                    TimeCreated = new DateTime(ticks++, DateTimeKind.Utc)
                });
            }
        }

        return [.. events];
    }

    private static ResolvedEvent ProcessImageEvent(long ticks, params (string Name, object? Value)[] fields) =>
        new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            Id = 4688,
            TimeCreated = new DateTime(ticks, DateTimeKind.Utc)
        }.WithEventData(fields);

    private static ResolvedEvent[] ProcessImageEvents(params (string NewProcessName, int Count)[] groups)
    {
        var events = new List<ResolvedEvent>();
        long ticks = 0;

        foreach ((string newProcessName, int count) in groups)
        {
            for (int index = 0; index < count; index++)
            {
                events.Add(ProcessImageEvent(ticks++, ("NewProcessName", newProcessName)));
            }
        }

        return [.. events];
    }

    private static ResolvedEvent ServicingUserDataEvent(string userDataPath, string errorCode, long ticks) =>
        new ResolvedEvent("TestLog", LogPathType.Channel) { Id = 3, Source = "Microsoft-Windows-Servicing", TimeCreated = new DateTime(ticks, DateTimeKind.Utc) }
            .WithUserData((userDataPath, errorCode));

    private static ResolvedEvent[] SourceEvents(params (string Source, int Count)[] groups)
    {
        var events = new List<ResolvedEvent>();
        long ticks = 0;

        foreach ((string source, int count) in groups)
        {
            for (int index = 0; index < count; index++)
            {
                events.Add(new ResolvedEvent("TestLog", LogPathType.Channel)
                {
                    Id = 0,
                    TimeCreated = new DateTime(ticks++, DateTimeKind.Utc),
                    Source = source
                });
            }
        }

        return [.. events];
    }

    private static int Sum(int[] counts)
    {
        int total = 0;

        foreach (int count in counts) { total += count; }

        return total;
    }
}

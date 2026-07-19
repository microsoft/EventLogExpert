// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Scenarios.Catalog;
using Fluxor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ScenarioLaunchServiceTests
{
    [Fact]
    public async Task LaunchAsync_ActivatingScenario_WhenTimelineAlreadyVisible_DoesNotReshow()
    {
        var (service, _, menu, scenario) =
            Create(new OpenLogsBatchResult(1, 0, 0, 0, []), activatesTimeline: true, timelineVisible: true);

        await service.LaunchAsync(scenario, dateWindow: null);

        menu.DidNotReceive().SetHistogramVisible(Arg.Any<bool>());
    }

    [Fact]
    public async Task LaunchAsync_ActivatingScenario_WhenTimelineHidden_ShowsTimeline()
    {
        var (service, _, menu, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []), activatesTimeline: true);

        await service.LaunchAsync(scenario, dateWindow: null);

        menu.Received(1).SetHistogramVisible(true);
    }

    [Fact]
    public async Task LaunchAsync_ActivatingScenario_ZeroOpenButCombining_ShowsTimeline()
    {
        var (service, _, menu, scenario) = Create(new OpenLogsBatchResult(0, 0, 0, 1, []), activatesTimeline: true);

        await service.LaunchAsync(scenario, dateWindow: null, combineLog: true);

        menu.Received(1).SetHistogramVisible(true);
    }

    [Fact]
    public async Task LaunchAsync_ActivatingScenario_ZeroOpenFreshView_DoesNotShowTimeline()
    {
        var (service, _, menu, scenario) =
            Create(new OpenLogsBatchResult(0, 1, 0, 0, ["System"]), activatesTimeline: true);

        await service.LaunchAsync(scenario, dateWindow: null);

        menu.DidNotReceive().SetHistogramVisible(Arg.Any<bool>());
    }

    [Fact]
    public async Task LaunchAsync_ActivatingScenarioWithoutTimelineDimension_DoesNotDispatchDimensionRequest()
    {
        var (service, dispatcher, _, scenario) =
            Create(new OpenLogsBatchResult(1, 0, 0, 0, []), activatesTimeline: true);

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<RequestHistogramDimensionAction>());
    }

    [Fact]
    public async Task LaunchAsync_ActivatingScenarioWithTimelineDimension_DispatchesDimensionRequest()
    {
        var (service, dispatcher, _, scenario) = Create(
            new OpenLogsBatchResult(1, 0, 0, 0, []),
            activatesTimeline: true,
            timelineDimension: ScenarioTimelineDimension.ErrorCode);

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.Received(1).Dispatch(
            Arg.Is<RequestHistogramDimensionAction>(action => action != null && action.Dimension == HistogramDimension.ErrorCode));
    }

    [Fact]
    public async Task LaunchAsync_ActivatingScenarioWithTimelineDimension_WhenAlreadyVisible_DispatchesDimensionRequest()
    {
        var (service, dispatcher, _, scenario) = Create(
            new OpenLogsBatchResult(1, 0, 0, 0, []),
            activatesTimeline: true,
            timelineVisible: true,
            timelineDimension: ScenarioTimelineDimension.EventId);

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.Received(1).Dispatch(
            Arg.Is<RequestHistogramDimensionAction>(action => action != null && action.Dimension == HistogramDimension.EventId));
    }

    [Fact]
    public async Task LaunchAsync_AppliesFiltersThenDate_BeforeOpening()
    {
        var (service, dispatcher, menu, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []));
        var window = new DateFilter { After = DateTime.UtcNow.AddDays(-7), Before = DateTime.UtcNow };

        await service.LaunchAsync(scenario, window);

        Received.InOrder(() =>
        {
            dispatcher.Dispatch(Arg.Any<ReplaceFiltersAction>());
            dispatcher.Dispatch(Arg.Any<SetFilterDateRangeAction>());
            _ = menu.OpenLiveLogsAsync(Arg.Any<IEnumerable<string>>(), false);
        });
    }

    [Fact]
    public async Task LaunchAsync_NonActivatingScenario_DoesNotShowTimeline()
    {
        var (service, _, menu, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchAsync(scenario, dateWindow: null);

        menu.DidNotReceive().SetHistogramVisible(Arg.Any<bool>());
    }

    [Fact]
    public async Task LaunchAsync_NullWindow_ClearsDateFilter()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.Received().Dispatch(Arg.Is<SetFilterDateRangeAction>(action => action != null && action.DateFilter == null));
    }

    [Fact]
    public async Task LaunchAsync_ReturnsOpenCounts()
    {
        var (service, _, _, scenario) = Create(new OpenLogsBatchResult(2, 1, 0, 0, []));

        var result = await service.LaunchAsync(scenario, dateWindow: null);

        Assert.Equal(2, result.Opened);
        Assert.Equal(1, result.Empty);
        Assert.True(result.AnyOpened);
    }

    [Fact]
    public async Task LaunchAsync_SomethingOpened_DoesNotDispatchCloseAll()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseAllLogsAction>());
    }

    [Fact]
    public async Task LaunchAsync_SomethingOpened_DoesNotRestoreFilters()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<RestoreFilterPaneStateAction>());
    }

    [Fact]
    public async Task LaunchAsync_ZeroOpenButCombining_DoesNotDispatchCloseAll()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(0, 0, 0, 1, []));

        await service.LaunchAsync(scenario, dateWindow: null, combineLog: true);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseAllLogsAction>());
    }

    [Fact]
    public async Task LaunchAsync_ZeroOpenButCombining_DoesNotRestoreFilters()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(0, 0, 0, 1, []));

        await service.LaunchAsync(scenario, dateWindow: null, combineLog: true);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<RestoreFilterPaneStateAction>());
    }

    [Fact]
    public async Task LaunchAsync_ZeroOpenFreshView_ClosesLogsBeforeRestoringFilters()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(0, 1, 0, 0, ["System"]));

        await service.LaunchAsync(scenario, dateWindow: null);

        Received.InOrder(() =>
        {
            dispatcher.Dispatch(Arg.Any<CloseAllLogsAction>());
            dispatcher.Dispatch(Arg.Any<RestoreFilterPaneStateAction>());
        });
    }

    [Fact]
    public async Task LaunchAsync_ZeroOpenFreshView_DispatchesCloseAll()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(0, 1, 0, 0, ["System"]));

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.Received(1).Dispatch(Arg.Any<CloseAllLogsAction>());
    }

    [Fact]
    public async Task LaunchAsync_ZeroOpenFreshView_RestoresFilterStateCapturedBeforeApply()
    {
        var scenario = ScenarioTestData.Single("a", "System", 1000);
        var registry = ScenarioTestData.Registry(scenario);

        var menu = Substitute.For<IMenuActionService>();
        menu.OpenLiveLogsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>())
            .Returns(new OpenLogsBatchResult(0, 1, 0, 0, ["System"]));

        var priorState = new FilterPaneState { Filters = [FilterBuilder.CreateTestFilter(isEnabled: false)] };
        var afterApplyState = new FilterPaneState { Filters = [FilterBuilder.CreateTestFilter(isEnabled: true)] };

        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(priorState);

        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.When(d => d.Dispatch(Arg.Any<ReplaceFiltersAction>()))
            .Do(_ => filterPaneState.Value.Returns(afterApplyState));

        var histogramState = Substitute.For<IState<HistogramState>>();
        histogramState.Value.Returns(new HistogramState());

        var folderPicker = Substitute.For<IFolderPickerService>();
        var folderEnumerator = Substitute.For<IEvtxFolderEnumerator>();
        var channelReader = Substitute.For<IEvtxChannelReader>();

        var service = new ScenarioLaunchService(
            registry, menu, filterPaneState, histogramState, folderPicker, folderEnumerator, channelReader, dispatcher);

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.Received(1)
            .Dispatch(Arg.Is<RestoreFilterPaneStateAction>(action => action != null && ReferenceEquals(action.State, priorState)));
    }

    [Fact]
    public async Task LaunchFromFolderAsync_ActivatingScenarioWhenHidden_ShowsTimeline()
    {
        var (service, _, menu, picker, enumerator, reader, scenario) =
            CreateFolder(FolderScenario() with { ActivatesTimeline = true }, timelineVisible: false);
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx"]));
        reader.ReadChannel("C:\\bundle\\System.evtx").Returns(EvtxChannelReadResult.FromChannel("System"));
        menu.OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), false).Returns(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        menu.Received(1).SetHistogramVisible(true);
    }

    [Fact]
    public async Task LaunchFromFolderAsync_CancelledDuringEnumeration_ReturnsCancelledWithoutOpening()
    {
        var (service, dispatcher, menu, picker, enumerator, _, scenario) = CreateFolder(FolderScenario());
        using var cts = new CancellationTokenSource();
        picker.PickFolderAsync().Returns("C:\\bundle");

        // The enumerator observes cancellation mid-scan and returns a normal (non-throwing) Empty result; the service must
        // re-check the token after the off-thread enumeration and report Cancelled instead of opening logs or alerting.
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();

                return EvtxFolderScanResult.Empty.Instance;
            });

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, cts.Token);

        Assert.Equal(ScenarioFolderOutcome.Cancelled, result.Outcome);
        await menu.DidNotReceive().OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public async Task LaunchFromFolderAsync_CancelledDuringProbe_ReturnsCancelledWithoutOpening()
    {
        var (service, dispatcher, menu, picker, enumerator, reader, scenario) = CreateFolder(FolderScenario());
        using var cts = new CancellationTokenSource();
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>())
            .Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx"]));

        // The per-file probe observes cancellation as it completes; the parallel loop still produces a match, so the
        // service must re-check after probing and report Cancelled rather than opening the matched log.
        reader.ReadChannel("C:\\bundle\\System.evtx").Returns(_ =>
        {
            cts.Cancel();

            return EvtxChannelReadResult.FromChannel("System");
        });

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, cts.Token);

        Assert.Equal(ScenarioFolderOutcome.Cancelled, result.Outcome);
        await menu.DidNotReceive().OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public async Task LaunchFromFolderAsync_CancelledToken_ReturnsCancelledWithoutOpening()
    {
        var (service, dispatcher, menu, picker, enumerator, _, scenario) = CreateFolder(FolderScenario());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>())
            .Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx"]));

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, cts.Token);

        Assert.Equal(ScenarioFolderOutcome.Cancelled, result.Outcome);
        await menu.DidNotReceive().OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public async Task LaunchFromFolderAsync_ActivatingScenarioWithoutTimelineDimension_DoesNotDispatchDimensionRequest()
    {
        var (service, dispatcher, menu, picker, enumerator, reader, scenario) =
            CreateFolder(FolderScenario() with { ActivatesTimeline = true });
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx"]));
        reader.ReadChannel("C:\\bundle\\System.evtx").Returns(EvtxChannelReadResult.FromChannel("System"));
        menu.OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), false).Returns(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<RequestHistogramDimensionAction>());
    }

    [Fact]
    public async Task LaunchFromFolderAsync_ActivatingScenarioWithTimelineDimension_DispatchesDimensionRequest()
    {
        var (service, dispatcher, menu, picker, enumerator, reader, scenario) =
            CreateFolder(FolderScenario() with
            {
                ActivatesTimeline = true,
                TimelineDimension = ScenarioTimelineDimension.Log
            });
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx"]));
        reader.ReadChannel("C:\\bundle\\System.evtx").Returns(EvtxChannelReadResult.FromChannel("System"));
        menu.OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), false).Returns(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        dispatcher.Received(1).Dispatch(
            Arg.Is<RequestHistogramDimensionAction>(action => action != null && action.Dimension == HistogramDimension.Log));
    }

    [Fact]
    public async Task LaunchFromFolderAsync_ActivatingScenarioWithTimelineDimension_WhenAlreadyVisible_DispatchesDimensionRequest()
    {
        var (service, dispatcher, menu, picker, enumerator, reader, scenario) =
            CreateFolder(
                FolderScenario() with
                {
                    ActivatesTimeline = true,
                    TimelineDimension = ScenarioTimelineDimension.Source
                },
                timelineVisible: true);
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx"]));
        reader.ReadChannel("C:\\bundle\\System.evtx").Returns(EvtxChannelReadResult.FromChannel("System"));
        menu.OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), false).Returns(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        dispatcher.Received(1).Dispatch(
            Arg.Is<RequestHistogramDimensionAction>(action => action != null && action.Dimension == HistogramDimension.Source));
    }

    [Fact]
    public async Task LaunchFromFolderAsync_CompletedWithUnreadableFile_PlumbsUnreadableCount()
    {
        var (service, _, menu, picker, enumerator, reader, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>())
            .Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx", "C:\\bundle\\bad.evtx"]));
        reader.ReadChannel("C:\\bundle\\System.evtx").Returns(EvtxChannelReadResult.FromChannel("System"));
        reader.ReadChannel("C:\\bundle\\bad.evtx").Returns(EvtxChannelReadResult.Unreadable);
        menu.OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), false).Returns(new OpenLogsBatchResult(1, 0, 0, 0, []));

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Unreadable);
    }

    [Fact]
    public async Task LaunchFromFolderAsync_EmptyFolder_ReturnsNoMatchingLogs()
    {
        var (service, _, menu, picker, enumerator, _, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(EvtxFolderScanResult.Empty.Instance);

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.NoMatchingLogs, result.Outcome);
        await menu.DidNotReceive().OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task LaunchFromFolderAsync_EnumerationAccessDenied_ReturnsError()
    {
        var (service, _, _, picker, enumerator, _, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.AccessDenied("access denied"));

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.Error, result.Outcome);
        Assert.Equal("access denied", result.Message);
    }

    [Fact]
    public async Task LaunchFromFolderAsync_MatchedButNoneOpened_RollsBackAndReturnsNoLogsOpened()
    {
        var (service, dispatcher, menu, picker, enumerator, reader, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx"]));
        reader.ReadChannel("C:\\bundle\\System.evtx").Returns(EvtxChannelReadResult.FromChannel("System"));
        menu.OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), false).Returns(new OpenLogsBatchResult(0, 1, 0, 0, []));

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.NoLogsOpened, result.Outcome);
        Assert.Equal(1, result.Empty);
        dispatcher.Received(1).Dispatch(Arg.Any<CloseAllLogsAction>());
        menu.DidNotReceive().SetHistogramVisible(Arg.Any<bool>());
    }

    [Fact]
    public async Task LaunchFromFolderAsync_MatchOpened_ReturnsCompletedAndAppliesFiltersToMatchedFiles()
    {
        var (service, dispatcher, menu, picker, enumerator, reader, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\System.evtx"]));
        reader.ReadChannel("C:\\bundle\\System.evtx").Returns(EvtxChannelReadResult.FromChannel("System"));
        menu.OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), false).Returns(new OpenLogsBatchResult(1, 0, 0, 0, []));

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.Opened);
        Assert.Equal(1, result.Matched);

        Received.InOrder(() =>
        {
            dispatcher.Dispatch(Arg.Any<ReplaceFiltersAction>());
            dispatcher.Dispatch(Arg.Any<SetFilterDateRangeAction>());
        });

        await menu.Received(1).OpenLogFilesAsync(
            Arg.Is<IEnumerable<string>>(paths => paths != null && paths.Single() == "C:\\bundle\\System.evtx"), false);
    }

    [Fact]
    public async Task LaunchFromFolderAsync_NoChannelMatch_ReturnsNoMatchingLogsWithMissingChannel()
    {
        var (service, _, menu, picker, enumerator, reader, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\other.evtx"]));
        reader.ReadChannel("C:\\bundle\\other.evtx").Returns(EvtxChannelReadResult.FromChannel("Application"));

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.NoMatchingLogs, result.Outcome);
        Assert.Contains("System", result.MissingChannels);
        await menu.DidNotReceive().OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task LaunchFromFolderAsync_NoMatchButUnreadable_ReturnsError()
    {
        var (service, _, _, picker, enumerator, reader, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().Returns("C:\\bundle");
        enumerator.EnumerateTopLevel("C:\\bundle", Arg.Any<CancellationToken>()).Returns(new EvtxFolderScanResult.Files(["C:\\bundle\\corrupt.evtx"]));
        reader.ReadChannel("C:\\bundle\\corrupt.evtx").Returns(EvtxChannelReadResult.Unreadable);

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.Error, result.Outcome);
        Assert.Equal(1, result.Unreadable);
    }

    [Fact]
    public async Task LaunchFromFolderAsync_PickerCancelled_ReturnsCancelledAndTouchesNothing()
    {
        var (service, dispatcher, menu, picker, _, _, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().Returns((string?)null);

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.Cancelled, result.Outcome);
        await menu.DidNotReceive().OpenLogFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public async Task LaunchFromFolderAsync_PickerThrows_ReturnsError()
    {
        var (service, _, _, picker, _, _, scenario) = CreateFolder(FolderScenario());
        picker.PickFolderAsync().ThrowsAsync(new InvalidOperationException("picker boom"));

        var result = await service.LaunchFromFolderAsync(scenario, dateWindow: null, TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioFolderOutcome.Error, result.Outcome);
        Assert.Equal("picker boom", result.Message);
    }

    [Theory]
    [InlineData(ScenarioTimelineDimension.Severity, HistogramDimension.Severity)]
    [InlineData(ScenarioTimelineDimension.Source, HistogramDimension.Source)]
    [InlineData(ScenarioTimelineDimension.EventId, HistogramDimension.EventId)]
    [InlineData(ScenarioTimelineDimension.TaskCategory, HistogramDimension.TaskCategory)]
    [InlineData(ScenarioTimelineDimension.Opcode, HistogramDimension.Opcode)]
    [InlineData(ScenarioTimelineDimension.Log, HistogramDimension.Log)]
    [InlineData(ScenarioTimelineDimension.LogonType, HistogramDimension.LogonType)]
    [InlineData(ScenarioTimelineDimension.TicketEncryptionType, HistogramDimension.TicketEncryptionType)]
    [InlineData(ScenarioTimelineDimension.ErrorCode, HistogramDimension.ErrorCode)]
    [InlineData(ScenarioTimelineDimension.ProcessImage, HistogramDimension.ProcessImage)]
    [InlineData(ScenarioTimelineDimension.ParentProcessImage, HistogramDimension.ParentProcessImage)]
    public void MapTimelineDimension_MapsEveryScenarioDimension(
        ScenarioTimelineDimension dimension,
        HistogramDimension expected)
    {
        var method = typeof(ScenarioLaunchService).GetMethod(
            "MapTimelineDimension",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [dimension]));
    }

    [Fact]
    public void ScenarioTimelineDimension_MatchesHistogramDimensionNames()
    {
        Assert.Equal(Enum.GetNames<ScenarioTimelineDimension>(), Enum.GetNames<HistogramDimension>());
    }

    private static (IScenarioLaunchService Service, IDispatcher Dispatcher, IMenuActionService Menu, ScenarioDefinition Scenario)
        Create(
            OpenLogsBatchResult openResult,
            bool activatesTimeline = false,
            bool timelineVisible = false,
            ScenarioTimelineDimension? timelineDimension = null)
    {
        var scenario = ScenarioTestData.Single("a", "System", 1000) with
        {
            ActivatesTimeline = activatesTimeline,
            TimelineDimension = timelineDimension
        };
        var registry = ScenarioTestData.Registry(scenario);

        var menu = Substitute.For<IMenuActionService>();
        menu.OpenLiveLogsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>()).Returns(openResult);

        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState());

        var histogramState = Substitute.For<IState<HistogramState>>();
        histogramState.Value.Returns(new HistogramState { IsVisible = timelineVisible });

        var folderPicker = Substitute.For<IFolderPickerService>();
        var folderEnumerator = Substitute.For<IEvtxFolderEnumerator>();
        var channelReader = Substitute.For<IEvtxChannelReader>();

        var dispatcher = Substitute.For<IDispatcher>();
        var service = new ScenarioLaunchService(
            registry, menu, filterPaneState, histogramState, folderPicker, folderEnumerator, channelReader, dispatcher);

        return (service, dispatcher, menu, scenario);
    }

    private static (IScenarioLaunchService Service, IDispatcher Dispatcher, IMenuActionService Menu,
        IFolderPickerService FolderPicker, IEvtxFolderEnumerator Enumerator, IEvtxChannelReader ChannelReader,
        ScenarioDefinition Scenario)
        CreateFolder(ScenarioDefinition scenario, bool timelineVisible = false)
    {
        var registry = ScenarioTestData.Registry(scenario);

        var menu = Substitute.For<IMenuActionService>();

        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState());

        var histogramState = Substitute.For<IState<HistogramState>>();
        histogramState.Value.Returns(new HistogramState { IsVisible = timelineVisible });

        var folderPicker = Substitute.For<IFolderPickerService>();
        var enumerator = Substitute.For<IEvtxFolderEnumerator>();
        var channelReader = Substitute.For<IEvtxChannelReader>();

        var dispatcher = Substitute.For<IDispatcher>();
        var service = new ScenarioLaunchService(
            registry, menu, filterPaneState, histogramState, folderPicker, enumerator, channelReader, dispatcher);

        return (service, dispatcher, menu, folderPicker, enumerator, channelReader, scenario);
    }

    private static ScenarioDefinition FolderScenario() => ScenarioTestData.Single("a", "System", 1000);
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Export;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Tests.TestUtils;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.Export;

public sealed class EventExportCoordinatorTests
{
    private const string LogName = "Application";

    private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(5);

    private readonly IEventTableExporter _exporter = Substitute.For<IEventTableExporter>();
    private readonly IFileSaveService _fileSave = Substitute.For<IFileSaveService>();
    private readonly IInfoBannerService _infoBanners = Substitute.For<IInfoBannerService>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IExportProgressBannerService _progress = Substitute.For<IExportProgressBannerService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private LogTableState _state = new();

    public EventExportCoordinatorTests()
    {
        _logTableState.Value.Returns(_ => _state);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task Export_KeepsTheRowsItCaptured_WhenTheDisplayMovesBeforeTheWriteBegins()
    {
        _state = StateWith(Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));

        LogTableState afterTheUserMovedOn = StateWith(Event(9, "Zeta"));

        _fileSave.SaveStreamingAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<Func<Stream, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                _state = afterTheUserMovedOn;

                await callInfo.ArgAt<Func<Stream, CancellationToken, Task>>(2)(Stream.Null, CancellationToken.None);

                return (string?)"C:\\events.csv";
            });

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        IEventColumnView written = ExportedRows()!;

        Assert.Equal(3, written.Count);
        Assert.Equal(1, afterTheUserMovedOn.GetActiveDisplayedEvents().Count);

        Assert.Equal(
            ["Alpha", "Beta", "Gamma"],
            written.EnumerateDetail().Select(@event => @event.Source).OrderBy(source => source, StringComparer.Ordinal));

        Assert.DoesNotContain("Zeta", written.EnumerateDetail().Select(@event => @event.Source));
    }

    [Fact]
    public async Task Export_TakesTheRowsAndTheColumnsThatDescribeThemFromOneRead()
    {
        LogTableState twoColumns = StateWith(Event(1, "Alpha")) with
        {
            Columns = ImmutableDictionary.CreateRange(new Dictionary<ColumnName, bool> { [ColumnName.Source] = true, [ColumnName.EventId] = true }),
            ColumnOrder = [ColumnName.Source, ColumnName.EventId]
        };

        LogTableState oneColumnLater = twoColumns with
        {
            Columns = ImmutableDictionary.CreateRange(new Dictionary<ColumnName, bool> { [ColumnName.Source] = true }),
            ColumnOrder = [ColumnName.Source]
        };

        bool firstRead = true;

        _logTableState.Value.Returns(_ =>
        {
            if (!firstRead) { return oneColumnLater; }

            firstRead = false;

            return twoColumns;
        });

        CaptureWrite();

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        Assert.Equal([ColumnName.Source, ColumnName.EventId], ExportedColumns()!);
    }

    [Fact]
    public async Task Export_WhenSaveIsCanceled_EmitsCanceledWarningBanner()
    {
        _state = StateWith(Event(1, "Alpha"));
        _progress.When(progress => progress.Begin(Arg.Any<Action>()))
            .Do(call => call.ArgAt<Action>(0).Invoke());
        _exporter.ExportAsync(
                Arg.Any<Stream>(),
                Arg.Any<ExportFormat>(),
                Arg.Any<IEventColumnView>(),
                Arg.Any<IReadOnlyList<ColumnName>>(),
                Arg.Any<TimeZoneInfo>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.ArgAt<CancellationToken>(6).ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        CaptureWrite();

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message => IsExportCanceled(message)),
            BannerSeverity.Warning);
    }

    [Fact]
    public async Task Export_WhenSaveThrows_EmitsFailedWarningBanner()
    {
        _state = StateWith(Event(1, "Alpha"));
        _fileSave.SaveStreamingAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<Func<Stream, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string?>(new InvalidOperationException("disk full")));

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message => IsExportFailed(message, "disk full")),
            BannerSeverity.Warning);
    }

    [Fact]
    public async Task Export_WhenTheEngineFailedButRowsAreStillOnScreen_WritesTheRowsTheUserCanSee()
    {
        _state = Reducers.ReduceOrderedViewDisplayFaulted(
            StateWith(Event(1, "Alpha")),
            new OrderedViewDisplayFaultedAction(new InvalidOperationException("bad predicate")));

        Assert.Equal(PresentationState.Faulted, _state.PresentationState);
        Assert.Equal(1, _state.GetActiveDisplayedEvents().Count);

        CaptureWrite();

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        await _fileSave.Received(1).SaveStreamingAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<Func<Stream, CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_WhileAnotherIsStillWriting_RefusesRatherThanOrphaningTheFirstCancellation()
    {
        _state = StateWith(Event(1, "Alpha"));

        var firstWriteReached = new TaskCompletionSource();
        var releaseFirstWrite = new TaskCompletionSource();

        _fileSave.SaveStreamingAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<Func<Stream, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                firstWriteReached.TrySetResult();

                await releaseFirstWrite.Task;

                return (string?)"C:\\events.csv";
            });

        EventExportCoordinator coordinator = CreateCoordinator();

        Task first = coordinator.ExportEventsAsync(ExportFormat.Csv);

        await firstWriteReached.Task;

        Task second = coordinator.ExportEventsAsync(ExportFormat.Csv);

        Task finished = await Task.WhenAny(
            second,
            Task.Delay(s_testTimeout, TestContext.Current.CancellationToken));

        Assert.True(ReferenceEquals(finished, second), "A second export was allowed to start while the first held the write.");

        await second;

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message =>
                IsExportBlocked(message, ExportBlockReason.AlreadyInProgress)),
            BannerSeverity.Warning);

        releaseFirstWrite.TrySetResult();

        await first;
    }

    [Fact]
    public async Task Export_WhileTheRowsAreStillBeingPrepared_SaysSoRatherThanClaimingThereAreNone()
    {
        _state = StateWith();

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        await _fileSave.DidNotReceive().SaveStreamingAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<Func<Stream, CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message =>
                IsExportBlocked(message, ExportBlockReason.Updating)),
            BannerSeverity.Warning);
    }

    [Fact]
    public async Task Export_WhileTheViewCouldNotBePrepared_SaysSoRatherThanClaimingThereAreNone()
    {
        _state = StateWith() with { OrderedViewDisplayEnabled = false, FaultCause = "InvalidOperationException: bad predicate" };

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        await _fileSave.DidNotReceive().SaveStreamingAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<Func<Stream, CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message =>
                IsExportBlocked(message, ExportBlockReason.Faulted)),
            BannerSeverity.Warning);
    }

    [Fact]
    public async Task Export_WithNoLogOpenAtAll_TellsTheUserAndNeverOpensTheSavePicker()
    {
        _state = StateWith() with { ActiveEventLogId = null };

        Assert.Null(_state.ServingOrderedView);
        Assert.Equal(PresentationState.Current, _state.PresentationState);

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        await _fileSave.DidNotReceive().SaveStreamingAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<Func<Stream, CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message =>
                IsExportBlocked(message, ExportBlockReason.NoEvents)),
            BannerSeverity.Warning);
    }

    [Fact]
    public async Task Export_WithNoRowsAndNothingLeftToWaitFor_TellsTheUserAndNeverOpensTheSavePicker()
    {
        _state = SettledEmpty();

        Assert.NotNull(_state.ServingOrderedView);
        Assert.Equal(PresentationState.Current, _state.PresentationState);

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        await _fileSave.DidNotReceive().SaveStreamingAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<Func<Stream, CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message =>
                IsExportBlocked(message, ExportBlockReason.NoEvents)),
            BannerSeverity.Warning);
    }

    [Fact]
    public async Task Export_WithNoVisibleColumns_TellsTheUserAndNeverOpensTheSavePicker()
    {
        _state = StateWith(Event(1, "Alpha")) with
        {
            Columns = ImmutableDictionary.CreateRange(new Dictionary<ColumnName, bool> { [ColumnName.Source] = false }),
            ColumnOrder = [ColumnName.Source]
        };

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        await _fileSave.DidNotReceive().SaveStreamingAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<Func<Stream, CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message =>
                IsExportBlocked(message, ExportBlockReason.NoColumns)),
            BannerSeverity.Warning);
    }

    [Fact]
    public async Task Export_WritesTheRowsTheDisplayIsShowing()
    {
        _state = StateWith(Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));
        CaptureWrite();

        await CreateCoordinator().ExportEventsAsync(ExportFormat.Csv);

        Assert.Equal(3, ExportedRows()!.Count);
        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message => IsExportComplete(message, 3, @"C:\events.csv")),
            BannerSeverity.Warning);
    }

    private static ResolvedEvent Event(long recordId, string source) =>
        new(LogName, LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(recordId),
            Id = 1000,
            Level = "Information",
            Source = source,
            LogName = LogName
        };

    private static bool IsExportBlocked(BannerMessage? message, ExportBlockReason reason) =>
        message is ExportBlocked exportBlocked &&
        exportBlocked.Reason == reason;

    private static bool IsExportCanceled(BannerMessage? message) =>
        message is ExportCanceled;

    private static bool IsExportComplete(BannerMessage? message, int count, string path) =>
        message is ExportComplete exportComplete &&
        exportComplete.Count == count &&
        exportComplete.Path == path;

    private static bool IsExportFailed(BannerMessage? message, string detail) =>
        message is ExportFailed exportFailed &&
        exportFailed.Detail == detail;

    private static LogTableState Serving(LogTableState state, EventLogId logId, IEventColumnView view) =>
        state with
        {
            ActiveOrderedView = new OrderedViewReady(
                SnapshotVersion: 1,
                Identity: state.ViewIdentity,
                Sequence: state.HighestInvalidationSequence,
                SingleLogId: logId,
                InScope: [new LogGeneration(logId, 0)],
                View: view,
                Config: state.SortContext,
                Filter: state.AppliedFilter)
        };

    private static LogTableState SettledEmpty()
    {
        LogTableState state = StateWith();

        return Serving(state, state.ActiveEventLogId!.Value, EmptyColumnView.Instance);
    }

    private static LogTableState StateWith(params ResolvedEvent[] events)
    {
        var logId = EventLogId.Create();

        var state = new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId) { LogName = LogName }],
            Columns = ImmutableDictionary.CreateRange(new Dictionary<ColumnName, bool> { [ColumnName.Source] = true }),
            ColumnOrder = [ColumnName.Source]
        };

        if (events.Length == 0) { return state; }

        return Serving(state, logId, DisplayViewTestFactory.Build(logId, events));
    }

    private void CaptureWrite() =>
        _fileSave.SaveStreamingAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                Arg.Any<Func<Stream, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await callInfo.ArgAt<Func<Stream, CancellationToken, Task>>(2)(Stream.Null, CancellationToken.None);

                return (string?)"C:\\events.csv";
            });

    private EventExportCoordinator CreateCoordinator() =>
        new(_logTableState,
            _exporter,
            _fileSave,
            _progress,
            _infoBanners,
            _settings,
            new ColumnDefaults(),
            Substitute.For<ITraceLogger>());

    private IReadOnlyList<ColumnName>? ExportedColumns() =>
        _exporter.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEventTableExporter.ExportAsync))
            .Select(call => (IReadOnlyList<ColumnName>)call.GetArguments()[3]!)
            .FirstOrDefault();

    private IEventColumnView? ExportedRows() =>
        _exporter.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEventTableExporter.ExportAsync))
            .Select(call => (IEventColumnView)call.GetArguments()[2]!)
            .FirstOrDefault();
}

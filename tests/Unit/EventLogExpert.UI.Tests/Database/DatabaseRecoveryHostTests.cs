// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Database;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.Modal;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EventLogExpert.UI.Tests.Database;

public sealed class DatabaseRecoveryHostTests
{
    private readonly ICriticalErrorService _criticalErrorService = Substitute.For<ICriticalErrorService>();
    private readonly IDatabaseService _databaseService = Substitute.For<IDatabaseService>();
    private readonly IErrorBannerService _errorBannerService = Substitute.For<IErrorBannerService>();
    private readonly IMainThreadService _mainThreadService = Substitute.For<IMainThreadService>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    private Func<Task>? _capturedRecoveryAction;
    private BannerId _nextBannerId = BannerId.Create();

    public DatabaseRecoveryHostTests()
    {
        _databaseService.Entries.Returns([]);
        _errorBannerService.ErrorBanners.Returns([]);

        _errorBannerService
            .ReportError(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Do<Func<Task>?>(action => _capturedRecoveryAction = action))
            .Returns(_ => _nextBannerId);

        _mainThreadService.InvokeOnMainThread(Arg.Any<Action>())
            .Returns(call =>
            {
                call.ArgAt<Action>(0)();
                return Task.CompletedTask;
            });

        _mainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>())
            .Returns(async call =>
            {
                await call.ArgAt<Func<Task>>(0)();
            });
    }

    [Fact]
    public void DatabaseRecoveryHost_BannerDismissedExternally_DoesNotRepromptForSameSet()
    {
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _errorBannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(initialId, "Database upgrade recovery", "...", "Resolve", null,
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))]);

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        _errorBannerService.ErrorBanners.Returns([]);
        _errorBannerService.StateChanged += Raise.Event<Action>();

        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_BannerDismissedExternally_NewBackupEntryAppears_RepromptsWithNewCount()
    {
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _errorBannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(initialId, "Database upgrade recovery", "...", "Resolve", null,
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))]);

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        _errorBannerService.ErrorBanners.Returns([]);
        _errorBannerService.StateChanged += Raise.Event<Action>();

        var newId = BannerId.Create();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_Ctor_DatabasesAlreadyHaveBackups_ReportsBannerImmediately()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();
        await Task.Yield();

        // Assert
        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "1 database needs recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_DialogOpenFails_ReportsCriticalViaBannerService()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var openFailure = new InvalidOperationException("coordinator unavailable");
        _modalCoordinator.PushAsync<DatabaseRecoveryModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .ThrowsAsync(openFailure);

        using var host = CreateHost();
        Assert.NotNull(_capturedRecoveryAction);

        await _capturedRecoveryAction!();

        _criticalErrorService.Received(1).ReportCritical(openFailure);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_DismissesOwnedBanner()
    {
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var host = CreateHost();

        host.Dispose();

        _errorBannerService.Received(1).DismissError(initialId);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_NoLongerRespondsToEntriesChanged()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var host = CreateHost();

        host.Dispose();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _errorBannerService.DidNotReceive().DismissError(Arg.Any<BannerId>());
        _errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_TwiceIsIdempotent()
    {
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var host = CreateHost();

        host.Dispose();
        host.Dispose();

        _errorBannerService.Received(1).DismissError(initialId);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_WithNoOwnedBanner_DoesNotCallDismiss()
    {
        _databaseService.Entries.Returns([]);

        var host = CreateHost();

        host.Dispose();

        _errorBannerService.DidNotReceive().DismissError(Arg.Any<BannerId>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_AllRecovered_DismissesBannerAndDoesNotReprompt()
    {
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _errorBannerService.Received(1).DismissError(initialId);
        _errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_DispatchFails_ReportsCritical()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var dispatchFailure = new InvalidOperationException("main thread unavailable");

        var entriesChangedDispatchCount = 0;
        _mainThreadService.InvokeOnMainThread(Arg.Any<Action>())
            .Returns(call =>
            {
                entriesChangedDispatchCount++;

                if (entriesChangedDispatchCount > 1)
                {
                    return Task.FromException(dispatchFailure);
                }

                call.ArgAt<Action>(0)();
                return Task.CompletedTask;
            });

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _criticalErrorService.Received(1).ReportCritical(dispatchFailure);
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_HandlerThrows_ReportsCritical()
    {
        var handlerFailure = new InvalidOperationException("entries unavailable");
        _databaseService.Entries.Returns(_ => throw handlerFailure);

        using var host = CreateHost();

        _criticalErrorService.Received(1).ReportCritical(handlerFailure);
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_NewBackupExistsEntry_DismissesOldBannerAndRaisesNewWithUpdatedCount()
    {
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();

        var newId = BannerId.Create();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _errorBannerService.Received(1).DismissError(initialId);
        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_SameBackupSet_DoesNotDismissOrReprompt()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _errorBannerService.DidNotReceive().DismissError(Arg.Any<BannerId>());
        _errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_ShrinkButStillNonEmpty_DismissesOldBannerAndRaisesNewWithUpdatedCount()
    {
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        using var host = CreateHost();

        var newId = BannerId.Create();
        _nextBannerId = newId;
        _databaseService.Entries.Returns([BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _errorBannerService.Received(1).DismissError(initialId);
        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "1 database needs recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_MultipleEntries_UsesPluralLabel()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        using var host = CreateHost();

        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_WithBackupExistsEntries_RaisesErrorBanner()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();

        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "1 database needs recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_WithNoBackupExistsEntries_DoesNotRaiseBanner()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", false)]);

        using var host = CreateHost();

        _errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_OpenRecoveryDialogAsync_EmptyEntries_DoesNotCallLauncher()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();
        Assert.NotNull(_capturedRecoveryAction);

        _databaseService.Entries.Returns([]);

        await _capturedRecoveryAction!();

        await _modalCoordinator.DidNotReceive().PushAsync<DatabaseRecoveryModal, bool>(
            Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_ResolveActionClicked_OpensDialogViaCoordinator()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        _modalCoordinator.PushAsync<DatabaseRecoveryModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        using var host = CreateHost();
        Assert.NotNull(_capturedRecoveryAction);

        await _capturedRecoveryAction!();

        await _modalCoordinator.Received(1).PushAsync<DatabaseRecoveryModal, bool>(
            Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_ResolveActionClicked_WhenPreempted_TracesAndDoesNotThrow()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        _modalCoordinator.PushAsync<DatabaseRecoveryModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: false));

        using var host = CreateHost();
        Assert.NotNull(_capturedRecoveryAction);

        await _capturedRecoveryAction!();

        await _modalCoordinator.Received(1).PushAsync<DatabaseRecoveryModal, bool>(
            Arg.Any<IDictionary<string, object?>?>());
        _traceLogger.Received().Trace(Arg.Any<TraceLogHandler>());
    }

    private static DatabaseEntry BuildEntry(string fileName, bool backupExists) =>
        new(
            fileName,
            $@"C:\dbs\{fileName}",
            false,
            DatabaseStatus.UpgradeRequired,
            backupExists);

    private DatabaseRecoveryHost CreateHost() =>
        new(_criticalErrorService, _errorBannerService, _databaseService, _modalCoordinator, _traceLogger, _mainThreadService);
}

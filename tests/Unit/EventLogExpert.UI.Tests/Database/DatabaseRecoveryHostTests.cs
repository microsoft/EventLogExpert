// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Database;
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
                ((Action)call[0])();
                return Task.CompletedTask;
            });

        _mainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>())
            .Returns(async call =>
            {
                await ((Func<Task>)call[0])();
            });
    }

    [Fact]
    public void DatabaseRecoveryHost_BannerDismissedExternally_DoesNotRepromptForSameSet()
    {
        // Arrange
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _errorBannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(initialId, "Database upgrade recovery", "...", "Resolve", null,
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))]);

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        // Act — banner disappears externally; host observes via StateChanged, then EntriesChanged for the same set.
        _errorBannerService.ErrorBanners.Returns([]);
        _errorBannerService.StateChanged += Raise.Event<Action>();

        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        _errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_BannerDismissedExternally_NewBackupEntryAppears_RepromptsWithNewCount()
    {
        // Arrange
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

        // Act
        var newId = BannerId.Create();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_Ctor_DatabasesAlreadyHaveBackups_ReportsBannerImmediately()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        // Act — construct host; ctor dispatches EvaluateState immediately to catch pre-existing state.
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
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var openFailure = new InvalidOperationException("coordinator unavailable");
        _modalCoordinator.PushAsync<DatabaseRecoveryDialog, bool>(Arg.Any<IDictionary<string, object?>?>())
            .ThrowsAsync(openFailure);

        using var host = CreateHost();
        Assert.NotNull(_capturedRecoveryAction);

        // Act
        await _capturedRecoveryAction!();

        // Assert
        _criticalErrorService.Received(1).ReportCritical(openFailure);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_DismissesOwnedBanner()
    {
        // Arrange
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var host = CreateHost();

        // Act
        host.Dispose();

        // Assert
        _errorBannerService.Received(1).DismissError(initialId);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_NoLongerRespondsToEntriesChanged()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var host = CreateHost();

        host.Dispose();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        // Act
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var host = CreateHost();

        // Act
        host.Dispose();
        host.Dispose();

        // Assert
        _errorBannerService.Received(1).DismissError(initialId);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_WithNoOwnedBanner_DoesNotCallDismiss()
    {
        // Arrange
        _databaseService.Entries.Returns([]);

        var host = CreateHost();

        // Act
        host.Dispose();

        // Assert
        _errorBannerService.DidNotReceive().DismissError(Arg.Any<BannerId>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_AllRecovered_DismissesBannerAndDoesNotReprompt()
    {
        // Arrange
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        // Act
        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var dispatchFailure = new InvalidOperationException("main thread unavailable");

        // Throw exclusively on the EntriesChanged dispatch (ctor's initial dispatch already used the default path).
        var entriesChangedDispatchCount = 0;
        _mainThreadService.InvokeOnMainThread(Arg.Any<Action>())
            .Returns(call =>
            {
                entriesChangedDispatchCount++;

                if (entriesChangedDispatchCount > 1)
                {
                    return Task.FromException(dispatchFailure);
                }

                ((Action)call[0])();
                return Task.CompletedTask;
            });

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        // Act
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        _criticalErrorService.Received(1).ReportCritical(dispatchFailure);
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_HandlerThrows_ReportsCritical()
    {
        // Arrange — Entries throws when read inside the handler.
        var handlerFailure = new InvalidOperationException("entries unavailable");
        _databaseService.Entries.Returns(_ => throw handlerFailure);

        using var host = CreateHost();

        // Assert — ctor's initial DispatchSafely(EvaluateState) already triggered the handler failure path.
        _criticalErrorService.Received(1).ReportCritical(handlerFailure);
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_NewBackupExistsEntry_DismissesOldBannerAndRaisesNewWithUpdatedCount()
    {
        // Arrange
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();

        // Act
        var newId = BannerId.Create();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();
        _criticalErrorService.ClearReceivedCalls();
        _errorBannerService.ClearReceivedCalls();

        // Act
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        var initialId = BannerId.Create();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        using var host = CreateHost();

        // Act
        var newId = BannerId.Create();
        _nextBannerId = newId;
        _databaseService.Entries.Returns([BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        // Act
        using var host = CreateHost();

        // Assert
        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_WithBackupExistsEntries_RaisesErrorBanner()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        // Act
        using var host = CreateHost();

        // Assert
        _errorBannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "1 database needs recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_WithNoBackupExistsEntries_DoesNotRaiseBanner()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", false)]);

        // Act
        using var host = CreateHost();

        // Assert
        _errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_OpenRecoveryDialogAsync_EmptyEntries_DoesNotCallLauncher()
    {
        // Arrange — start with backups, then drain them after the banner is reported but before the CTA fires.
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        using var host = CreateHost();
        Assert.NotNull(_capturedRecoveryAction);

        _databaseService.Entries.Returns([]);

        // Act
        await _capturedRecoveryAction!();

        // Assert
        await _modalCoordinator.DidNotReceive().PushAsync<DatabaseRecoveryDialog, bool>(
            Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_ResolveActionClicked_OpensDialogViaCoordinator()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        _modalCoordinator.PushAsync<DatabaseRecoveryDialog, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        using var host = CreateHost();
        Assert.NotNull(_capturedRecoveryAction);

        // Act
        await _capturedRecoveryAction!();

        // Assert
        await _modalCoordinator.Received(1).PushAsync<DatabaseRecoveryDialog, bool>(
            Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_ResolveActionClicked_WhenPreempted_TracesAndDoesNotThrow()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        _modalCoordinator.PushAsync<DatabaseRecoveryDialog, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: false));

        using var host = CreateHost();
        Assert.NotNull(_capturedRecoveryAction);

        // Act
        await _capturedRecoveryAction!();

        // Assert
        await _modalCoordinator.Received(1).PushAsync<DatabaseRecoveryDialog, bool>(
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

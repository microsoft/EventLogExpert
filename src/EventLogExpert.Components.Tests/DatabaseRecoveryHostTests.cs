// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.Components.Tests;

public sealed class DatabaseRecoveryHostTests : BunitContext
{
    private readonly IBannerService _bannerService = Substitute.For<IBannerService>();
    private readonly IDatabaseService _databaseService = Substitute.For<IDatabaseService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    private Func<Task>? _capturedRecoveryAction;
    private Guid _nextBannerId = Guid.NewGuid();

    public DatabaseRecoveryHostTests()
    {
        _databaseService.Entries.Returns([]);
        _bannerService.ErrorBanners.Returns([]);

        // Capture the Resolve action callback as a side-effect of any ReportError call so dialog-
        // open tests can invoke it without having to re-discover it. _nextBannerId is read inside
        // the Returns lambda so each test can stage a distinct id per call.
        _bannerService
            .ReportError(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Do<Func<Task>?>(action => _capturedRecoveryAction = action))
            .Returns(_ => _nextBannerId);

        Services.AddSingleton(_bannerService);
        Services.AddSingleton(_databaseService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void DatabaseRecoveryHost_BannerDismissedExternally_DoesNotRepromptForSameSet()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _bannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(initialId, "Database upgrade recovery", "...", "Resolve", null,
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))]);

        Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        // User clicks the banner's X button; ErrorBanners no longer contains our id.
        _bannerService.ErrorBanners.Returns([]);
        _bannerService.StateChanged += Raise.Event<Action>();

        // Subsequent EntriesChanged tick with the SAME set must not re-prompt.
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_BannerDismissedExternally_NewBackupEntryAppears_RepromptsWithNewCount()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _bannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(initialId, "Database upgrade recovery", "...", "Resolve", null,
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))]);

        Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        // External dismissal.
        _bannerService.ErrorBanners.Returns([]);
        _bannerService.StateChanged += Raise.Event<Action>();

        // A new backup-exists entry appears later.
        var newId = Guid.NewGuid();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_DialogDismissed_BannerStillVisible_DoesNotDismissBanner()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        await component.InvokeAsync(() => _capturedRecoveryAction!());

        await component.Find("button:contains('Cancel')").ClickAsync(new());

        Assert.Empty(component.FindAll("dialog"));

        // The banner stays — host did NOT call DismissError as part of dialog dismissal.
        _bannerService.DidNotReceive().DismissError(Arg.Any<Guid>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_DialogOpen_NewBackupEntryAppears_RefreshesBannerKeepsDialogOpen()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();
        await component.InvokeAsync(() => _capturedRecoveryAction!());

        Assert.Single(component.FindAll("dialog"));

        var newId = Guid.NewGuid();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _bannerService.Received(1).DismissError(initialId);
        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());

        Assert.Single(component.FindAll("dialog"));
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_DismissesOwnedBanner()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();

        component.Instance.Dispose();

        // Without this, a crash that triggers UnhandledExceptionHandler.OnErrorAsync would dispose
        // the host but leave the recovery banner alive in BannerService.ErrorBanners — the new host
        // mounted after Recover() would then post a second banner alongside the stale one whose
        // Resolve action targets this dead instance.
        _bannerService.Received(1).DismissError(initialId);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_NoLongerRespondsToEntriesChanged()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();

        component.Instance.Dispose();
        _bannerService.ClearReceivedCalls();

        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _bannerService.DidNotReceive().DismissError(Arg.Any<Guid>());
        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_TwiceIsIdempotent()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();

        component.Instance.Dispose();
        component.Instance.Dispose();

        _bannerService.Received(1).DismissError(initialId);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_WithNoOwnedBanner_DoesNotCallDismiss()
    {
        _databaseService.Entries.Returns([]);

        var component = Render<DatabaseRecoveryHost>();

        component.Instance.Dispose();

        _bannerService.DidNotReceive().DismissError(Arg.Any<Guid>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_AllRecovered_DismissesBannerAndDoesNotReprompt()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _bannerService.Received(1).DismissError(initialId);
        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_NewBackupExistsEntry_DismissesOldBannerAndRaisesNewWithUpdatedCount()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();

        var newId = Guid.NewGuid();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _bannerService.Received(1).DismissError(initialId);
        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_SameBackupSet_DoesNotDismissOrReprompt()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        // Same set, fresh EntriesChanged tick (e.g., unrelated entry's IsEnabled toggled).
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _bannerService.DidNotReceive().DismissError(Arg.Any<Guid>());
        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_ShrinkButStillNonEmpty_DismissesOldBannerAndRaisesNewWithUpdatedCount()
    {
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();

        // a.db gets resolved externally; b.db remains.
        var newId = Guid.NewGuid();
        _nextBannerId = newId;
        _databaseService.Entries.Returns([BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        _bannerService.Received(1).DismissError(initialId);
        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "1 database needs recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_MultipleEntries_UsesPluralLabel()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();

        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_WithBackupExistsEntries_RaisesErrorBanner()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();

        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "1 database needs recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_WithNoBackupExistsEntries_DoesNotRaiseBanner()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: false)]);

        Render<DatabaseRecoveryHost>();

        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_ResolveActionClicked_OpensDialog()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();

        Assert.NotNull(_capturedRecoveryAction);
        Assert.Empty(component.FindAll("dialog"));

        await component.InvokeAsync(() => _capturedRecoveryAction!());

        Assert.Single(component.FindAll("dialog"));
    }

    private static DatabaseEntry BuildEntry(string fileName, bool backupExists) =>
        new(
            fileName,
            $@"C:\dbs\{fileName}",
            IsEnabled: false,
            DatabaseStatus.UpgradeRequired,
            BackupExists: backupExists);
}

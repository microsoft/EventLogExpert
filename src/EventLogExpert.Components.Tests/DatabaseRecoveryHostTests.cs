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
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _bannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(initialId, "Database upgrade recovery", "...", "Resolve", null,
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))]);

        Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        // Act
        _bannerService.ErrorBanners.Returns([]);
        _bannerService.StateChanged += Raise.Event<Action>();

        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_BannerDismissedExternally_NewBackupEntryAppears_RepromptsWithNewCount()
    {
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _bannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(initialId, "Database upgrade recovery", "...", "Resolve", null,
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))]);

        Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        _bannerService.ErrorBanners.Returns([]);
        _bannerService.StateChanged += Raise.Event<Action>();

        // Act
        var newId = Guid.NewGuid();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_DialogDismissed_BannerStillVisible_DoesNotDismissBanner()
    {
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        await component.InvokeAsync(() => _capturedRecoveryAction!());

        // Act
        await component.Find("button:contains('Cancel')").ClickAsync(new());

        // Assert
        Assert.Empty(component.FindAll("dialog"));

        _bannerService.DidNotReceive().DismissError(Arg.Any<Guid>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_DialogOpen_NewBackupEntryAppears_RefreshesBannerKeepsDialogOpen()
    {
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();
        await component.InvokeAsync(() => _capturedRecoveryAction!());

        Assert.Single(component.FindAll("dialog"));

        // Act
        var newId = Guid.NewGuid();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();

        // Act
        component.Instance.Dispose();

        // Assert
        _bannerService.Received(1).DismissError(initialId);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_NoLongerRespondsToEntriesChanged()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();

        component.Instance.Dispose();
        _bannerService.ClearReceivedCalls();

        // Act
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();

        // Act
        component.Instance.Dispose();
        component.Instance.Dispose();

        // Assert
        _bannerService.Received(1).DismissError(initialId);
    }

    [Fact]
    public void DatabaseRecoveryHost_Disposed_WithNoOwnedBanner_DoesNotCallDismiss()
    {
        // Arrange
        _databaseService.Entries.Returns([]);

        var component = Render<DatabaseRecoveryHost>();

        // Act
        component.Instance.Dispose();

        // Assert
        _bannerService.DidNotReceive().DismissError(Arg.Any<Guid>());
    }

    [Fact]
    public void DatabaseRecoveryHost_EntriesChanged_AllRecovered_DismissesBannerAndDoesNotReprompt()
    {
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        // Act
        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();

        // Act
        var newId = Guid.NewGuid();
        _nextBannerId = newId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();
        _bannerService.ClearReceivedCalls();

        // Act
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        var initialId = Guid.NewGuid();
        _nextBannerId = initialId;
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        Render<DatabaseRecoveryHost>();

        // Act
        var newId = Guid.NewGuid();
        _nextBannerId = newId;
        _databaseService.Entries.Returns([BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
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
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        // Act
        Render<DatabaseRecoveryHost>();

        // Assert
        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "2 databases need recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_WithBackupExistsEntries_RaisesErrorBanner()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        // Act
        Render<DatabaseRecoveryHost>();

        // Assert
        _bannerService.Received(1).ReportError(
            "Database upgrade recovery",
            "1 database needs recovery from interrupted upgrade.",
            "Resolve",
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public void DatabaseRecoveryHost_OnInit_WithNoBackupExistsEntries_DoesNotRaiseBanner()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: false)]);

        // Act
        Render<DatabaseRecoveryHost>();

        // Assert
        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task DatabaseRecoveryHost_ResolveActionClicked_OpensDialog()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryHost>();

        Assert.NotNull(_capturedRecoveryAction);
        Assert.Empty(component.FindAll("dialog"));

        // Act
        await component.InvokeAsync(() => _capturedRecoveryAction!());

        // Assert
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

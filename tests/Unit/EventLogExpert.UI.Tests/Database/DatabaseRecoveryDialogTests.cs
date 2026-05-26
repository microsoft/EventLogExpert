// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Database;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Database;

public sealed class DatabaseRecoveryDialogTests : BunitContext
{
    private readonly IBannerService _bannerService = Substitute.For<IBannerService>();
    private readonly IDatabaseService _databaseService = Substitute.For<IDatabaseService>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ModalId _modalId = new(1L);
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    private ModalRegistration? _capturedRegistration;

    public DatabaseRecoveryDialogTests()
    {
        _databaseService.Entries.Returns([]);
        _modalService.ActiveModalId.Returns(_modalId);

        _modalCoordinator
            .When(coordinator => coordinator.RegisterModal(Arg.Any<ModalRegistration>()))
            .Do(call => _capturedRegistration = call.Arg<ModalRegistration>());

        Services.AddSingleton(_bannerService);
        Services.AddSingleton(_databaseService);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_traceLogger);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(DatabaseRecoveryDialog).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_AllRowsSucceed_AutoCompletesWithFalseWhenEntriesDrain()
    {
        // Arrange
        var entriesBefore = new[]
        {
            BuildEntry("a.db", true),
            BuildEntry("b.db", true)
        };

        _databaseService.Entries.Returns(entriesBefore);

        _databaseService.RestoreFromBackupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        await component.WaitForAssertionAsync(() =>
            _modalService.Received().Complete(_modalId, Arg.Is<object?>(value => Equals(value, false))));
        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyDeleteReturnsFalse_SurfacesErrorBannerAndMarksRowFailed()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button.button:contains('Delete all')").ClickAsync(new MouseEventArgs());
        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        _bannerService.Received(1).ReportError(
            "Database recovery failed",
            "Failed to delete 'a.db'.");

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
        _modalService.DidNotReceive().Complete(Arg.Any<ModalId>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyDisablesAllControls_WhilePending()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        var pendingRestore = new TaskCompletionSource<bool>();
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(pendingRestore.Task);

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        var applyClick = component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        Assert.True(((IHtmlButtonElement)component.Find("button:contains('Apply')")).IsDisabled);
        Assert.True(((IHtmlButtonElement)component.Find("button:contains('Cancel')")).IsDisabled);
        Assert.True(((IHtmlButtonElement)component.Find("button.button:contains('Restore all')")).IsDisabled);
        Assert.True(((IHtmlButtonElement)component.Find("button.button:contains('Delete all')")).IsDisabled);

        foreach (var radio in component.FindAll("li.recovery-row input[type=radio]"))
        {
            Assert.True(((IHtmlInputElement)radio).IsDisabled);
        }

        pendingRestore.SetResult(true);
        await applyClick;
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyMixed_CallsBothMethodsForRespectiveRows()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _databaseService.DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        var bRow = component.FindAll("li.recovery-row")[1];
        var bDeleteRadio = bRow.QuerySelectorAll("input[type=radio]")[1];
        await bDeleteRadio.ChangeAsync(new ChangeEventArgs { Value = "on" });

        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyRestoreReturnsFalse_SurfacesErrorBannerAndMarksRowFailed()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        _bannerService.Received(1).ReportError(
            "Database recovery failed",
            "Failed to restore 'a.db' from backup.");

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
        _modalService.DidNotReceive().Complete(Arg.Any<ModalId>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyThrowsInvalidOperation_TreatsAsBenignSkipNoErrorBanner()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("entry not found")));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.DoesNotContain("recovery-row-failed", rowClass);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyThrowsUnexpected_SurfacesErrorBannerAndMarksRowFailed()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new IOException("disk gone")));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        _bannerService.Received(1).ReportError(
            "Database recovery failed",
            "Failed to restore 'a.db' from backup.");

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyWithDelete_CallsDeleteEntryWithBackupAsyncWithFileName()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button.button:contains('Delete all')").ClickAsync(new MouseEventArgs());
        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        await _databaseService.Received(1).DeleteEntryWithBackupAsync(
            Arg.Is<string>(name => name == "a.db"),
            Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().RestoreFromBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyWithRestore_CallsRestoreFromBackupAsyncWithFileName()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        await _databaseService.Received(1).RestoreFromBackupAsync(
            Arg.Is<string>(name => name == "a.db"),
            Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_CancelClicked_RoutesThroughCoordinatorAndDoesNotCallDatabaseService()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Cancel')").ClickAsync(new MouseEventArgs());

        // Assert
        await _modalCoordinator.Received(1).RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        await _databaseService.DidNotReceive().RestoreFromBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DatabaseRecoveryDialog_DefaultsEachRowToRestore()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        // Act
        var component = Render<DatabaseRecoveryDialog>();

        // Assert
        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.Equal(2, radios.Length);
            Assert.True(((IHtmlInputElement)radios[0]).IsChecked);
            Assert.False(((IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_DeleteAllClicked_SetsAllRowsToDelete()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button.button:contains('Delete all')").ClickAsync(new MouseEventArgs());

        // Assert
        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.False(((IHtmlInputElement)radios[0]).IsChecked);
            Assert.True(((IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_EntriesChangedAllResolved_AutoCompletesWithFalse()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        await component.WaitForAssertionAsync(() =>
            _modalService.Received().Complete(_modalId, Arg.Is<object?>(value => Equals(value, false))));
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_EntriesChangedNewBackupExistsEntry_AddsRowWithRestoreDefault()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        component.WaitForState(() => component.FindAll("li.recovery-row").Count == 2);

        // Assert
        var newRow = component.FindAll("li.recovery-row")[1];
        Assert.Contains("b.db", newRow.TextContent);

        var radios = newRow.QuerySelectorAll("input[type=radio]");
        Assert.True(((IHtmlInputElement)radios[0]).IsChecked);
        Assert.False(((IHtmlInputElement)radios[1]).IsChecked);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_EntriesChangedSubsetResolved_RemovesResolvedRowsKeepsDialogOpen()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        var component = Render<DatabaseRecoveryDialog>();

        var bRow = component.FindAll("li.recovery-row")[1];
        var bDeleteRadio = bRow.QuerySelectorAll("input[type=radio]")[1];
        await bDeleteRadio.ChangeAsync(new ChangeEventArgs { Value = "on" });

        // Act
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        component.WaitForState(() => component.FindAll("li.recovery-row").Count == 1);

        // Assert
        var remainingRow = component.Find("li.recovery-row");
        Assert.Contains("a.db", remainingRow.TextContent);

        var radios = remainingRow.QuerySelectorAll("input[type=radio]");
        Assert.True(((IHtmlInputElement)radios[0]).IsChecked);
        Assert.False(((IHtmlInputElement)radios[1]).IsChecked);
        _modalService.DidNotReceive().Complete(Arg.Any<ModalId>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_FailedRow_LosesFailureMarkOnNextApplySuccessWhileOthersStayFailed()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        _databaseService.RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryDialog>();

        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "a.db").GetAttribute("class") ?? string.Empty);
        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "b.db").GetAttribute("class") ?? string.Empty);

        // Act
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        Assert.DoesNotContain(
            "recovery-row-failed",
            FindRowByFileName(component, "a.db").GetAttribute("class") ?? string.Empty);
        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "b.db").GetAttribute("class") ?? string.Empty);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_InitialEmptySet_AutoCompletesWithoutShowingModal()
    {
        // Arrange
        _databaseService.Entries.Returns([]);

        // Act
        var component = Render<DatabaseRecoveryDialog>();

        // Assert
        await component.WaitForAssertionAsync(() =>
            _modalService.Received().Complete(_modalId, Arg.Is<object?>(value => Equals(value, false))));
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_OnRequestCloseAsync_WhenIdle_ReturnsTrue()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        Render<DatabaseRecoveryDialog>();
        Assert.NotNull(_capturedRegistration);

        // Act
        bool accepted = await _capturedRegistration!.RequestClose(
            new ModalCloseRequest(ModalCloseReason.UserDismiss));

        // Assert
        Assert.True(accepted);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_OnRequestCloseAsync_WhileApplying_ReturnsFalse()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        var pendingRestore = new TaskCompletionSource<bool>();
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(pendingRestore.Task);

        var component = Render<DatabaseRecoveryDialog>();
        Assert.NotNull(_capturedRegistration);

        // Act — start Apply (sets _isApplying), then ask the registration whether it can close.
        var applyClick = component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        bool accepted = await _capturedRegistration!.RequestClose(
            new ModalCloseRequest(ModalCloseReason.UserDismiss));

        // Assert
        Assert.False(accepted);

        pendingRestore.SetResult(true);
        await applyClick;
    }

    [Fact]
    public void DatabaseRecoveryDialog_RegistersAsCriticalScope()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        // Act
        Render<DatabaseRecoveryDialog>();

        // Assert
        Assert.NotNull(_capturedRegistration);
        Assert.Equal(ModalScope.Critical, _capturedRegistration!.Scope);
    }

    [Fact]
    public void DatabaseRecoveryDialog_RendersOneRowPerBackupExistsEntry()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [
                BuildEntry("a.db", true),
                BuildEntry("b.db", true),
                BuildEntry("c.db", false)
            ]);

        // Act
        var component = Render<DatabaseRecoveryDialog>();

        // Assert
        var rows = component.FindAll("li.recovery-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("a.db", rows[0].TextContent);
        Assert.Contains("b.db", rows[1].TextContent);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_RestoreAllClicked_SetsAllRowsToRestore()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        var component = Render<DatabaseRecoveryDialog>();

        var rowCount = component.FindAll("li.recovery-row").Count;
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var deleteRadio = component.FindAll("li.recovery-row")[rowIndex]
                .QuerySelectorAll("input[type=radio]")[1];
            await deleteRadio.ChangeAsync(new ChangeEventArgs { Value = "on" });
        }

        // Act
        await component.Find("button.button:contains('Restore all')").ClickAsync(new MouseEventArgs());

        // Assert
        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.True(((IHtmlInputElement)radios[0]).IsChecked);
            Assert.False(((IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_RowResolvedExternallyMidLoop_DoesNotCallServiceForResolvedRow()
    {
        // Arrange
        var entriesBefore = new[]
        {
            BuildEntry("a.db", true),
            BuildEntry("b.db", true)
        };

        var entriesAfter = new[]
        {
            BuildEntry("a.db", true),
            BuildEntry("b.db", false)
        };

        _databaseService.Entries.Returns(entriesBefore);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        _databaseService.Entries.Returns(entriesAfter);

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new MouseEventArgs());

        // Assert
        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
        _bannerService.DidNotReceive().ReportError(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<Task>?>());
    }

    private static DatabaseEntry BuildEntry(string fileName, bool backupExists) =>
        new(
            fileName,
            $@"C:\dbs\{fileName}",
            false,
            DatabaseStatus.UpgradeRequired,
            backupExists);

    private static IElement FindRowByFileName(
        IRenderedComponent<DatabaseRecoveryDialog> component,
        string fileName)
    {
        foreach (var row in component.FindAll("li.recovery-row"))
        {
            if (row.TextContent.Contains(fileName, StringComparison.Ordinal))
            {
                return row;
            }
        }

        throw new InvalidOperationException(
            $"No recovery row found whose text contains '{fileName}'.");
    }
}

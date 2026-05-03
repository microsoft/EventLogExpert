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

public sealed class DatabaseRecoveryDialogTests : BunitContext
{
    private readonly IBannerService _bannerService = Substitute.For<IBannerService>();
    private readonly IDatabaseService _databaseService = Substitute.For<IDatabaseService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    private int _onDismissedCallCount;

    public DatabaseRecoveryDialogTests()
    {
        _databaseService.Entries.Returns([]);

        Services.AddSingleton(_bannerService);
        Services.AddSingleton(_databaseService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_AllRowsSucceed_AutoDismisses()
    {
        // Arrange
        var entriesBefore = new DatabaseEntry[]
        {
            BuildEntry("a.db", backupExists: true),
            BuildEntry("b.db", backupExists: true)
        };

        _databaseService.Entries.Returns(entriesBefore);

        _databaseService.RestoreFromBackupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new());

        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        await component.WaitForAssertionAsync(() => Assert.Equal(1, _onDismissedCallCount));
        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyDeleteReturnsFalse_SurfacesErrorBannerAndMarksRowFailed()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Act
        await component.Find("button.button:contains('Delete all')").ClickAsync(new());
        await component.Find("button:contains('Apply')").ClickAsync(new());

        // Assert
        _bannerService.Received(1).ReportError(
            "Database recovery failed",
            "Failed to delete 'a.db'.");

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
        Assert.Equal(0, _onDismissedCallCount);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyDisablesAllControls_WhilePending()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        var pendingRestore = new TaskCompletionSource<bool>();
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(pendingRestore.Task);

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        var applyClick = component.Find("button:contains('Apply')").ClickAsync(new());

        // Assert
        Assert.True(((AngleSharp.Html.Dom.IHtmlButtonElement)component.Find("button:contains('Apply')")).IsDisabled);
        Assert.True(((AngleSharp.Html.Dom.IHtmlButtonElement)component.Find("button:contains('Cancel')")).IsDisabled);
        Assert.True(((AngleSharp.Html.Dom.IHtmlButtonElement)component.Find("button.button:contains('Restore all')")).IsDisabled);
        Assert.True(((AngleSharp.Html.Dom.IHtmlButtonElement)component.Find("button.button:contains('Delete all')")).IsDisabled);

        foreach (var radio in component.FindAll("li.recovery-row input[type=radio]"))
        {
            Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)radio).IsDisabled);
        }

        pendingRestore.SetResult(true);
        await applyClick;
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyMixed_CallsBothMethodsForRespectiveRows()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _databaseService.DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        var bRow = component.FindAll("li.recovery-row")[1];
        var bDeleteRadio = bRow.QuerySelectorAll("input[type=radio]")[1];
        await bDeleteRadio.ChangeAsync(new() { Value = "on" });

        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new());

        // Assert
        _bannerService.Received(1).ReportError(
            "Database recovery failed",
            "Failed to restore 'a.db' from backup.");

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
        Assert.Equal(0, _onDismissedCallCount);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyThrowsInvalidOperation_TreatsAsBenignSkipNoErrorBanner()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("entry not found")));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new IOException("disk gone")));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button.button:contains('Delete all')").ClickAsync(new());
        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new());

        // Assert
        await _databaseService.Received(1).RestoreFromBackupAsync(
            Arg.Is<string>(name => name == "a.db"),
            Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_CancelClicked_RaisesOnDismissedDoesNotCallDatabaseService()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Act
        await component.Find("button:contains('Cancel')").ClickAsync(new());

        // Assert
        Assert.Equal(1, _onDismissedCallCount);
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
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        // Act
        var component = Render<DatabaseRecoveryDialog>();

        // Assert
        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.Equal(2, radios.Length);
            Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)radios[0]).IsChecked);
            Assert.False(((AngleSharp.Html.Dom.IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_DeleteAllClicked_SetsAllRowsToDelete()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        await component.Find("button.button:contains('Delete all')").ClickAsync(new());

        // Assert
        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.False(((AngleSharp.Html.Dom.IHtmlInputElement)radios[0]).IsChecked);
            Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_EntriesChangedAllResolved_AutoDismisses()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Act
        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        // Assert
        await component.WaitForAssertionAsync(() => Assert.Equal(1, _onDismissedCallCount));
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_EntriesChangedNewBackupExistsEntry_AddsRowWithRestoreDefault()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>();

        // Act
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        component.WaitForState(() => component.FindAll("li.recovery-row").Count == 2);

        // Assert
        var newRow = component.FindAll("li.recovery-row")[1];
        Assert.Contains("b.db", newRow.TextContent);

        var radios = newRow.QuerySelectorAll("input[type=radio]");
        Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)radios[0]).IsChecked);
        Assert.False(((AngleSharp.Html.Dom.IHtmlInputElement)radios[1]).IsChecked);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_EntriesChangedSubsetResolved_RemovesResolvedRowsKeepsDialogOpen()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        var bRow = component.FindAll("li.recovery-row")[1];
        var bDeleteRadio = bRow.QuerySelectorAll("input[type=radio]")[1];
        await bDeleteRadio.ChangeAsync(new() { Value = "on" });

        // Act
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        component.WaitForState(() => component.FindAll("li.recovery-row").Count == 1);

        // Assert
        var remainingRow = component.Find("li.recovery-row");
        Assert.Contains("a.db", remainingRow.TextContent);

        var radios = remainingRow.QuerySelectorAll("input[type=radio]");
        Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)radios[0]).IsChecked);
        Assert.False(((AngleSharp.Html.Dom.IHtmlInputElement)radios[1]).IsChecked);
        Assert.Equal(0, _onDismissedCallCount);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_EscDuringApply_DoesNotDismiss()
    {
        // Arrange
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        var pendingRestore = new TaskCompletionSource<bool>();
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(pendingRestore.Task);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Act
        var applyTask = component.Find("button:contains('Apply')").ClickAsync(new());

        await component.Find("dialog").TriggerEventAsync("oncancel", EventArgs.Empty);

        // Assert
        Assert.Equal(0, _onDismissedCallCount);

        pendingRestore.SetResult(true);
        await applyTask;
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_FailedRow_LosesFailureMarkOnNextApplySuccessWhileOthersStayFailed()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        _databaseService.RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryDialog>();

        await component.Find("button:contains('Apply')").ClickAsync(new());

        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "a.db").GetAttribute("class") ?? string.Empty);
        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "b.db").GetAttribute("class") ?? string.Empty);

        // Act
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await component.Find("button:contains('Apply')").ClickAsync(new());

        // Assert
        Assert.DoesNotContain(
            "recovery-row-failed",
            FindRowByFileName(component, "a.db").GetAttribute("class") ?? string.Empty);
        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "b.db").GetAttribute("class") ?? string.Empty);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_InitialEmptySet_AutoDismissesWithoutShowingModal()
    {
        // Arrange
        _databaseService.Entries.Returns([]);

        // Act
        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Assert
        await component.WaitForAssertionAsync(() => Assert.Equal(1, _onDismissedCallCount));
    }

    [Fact]
    public void DatabaseRecoveryDialog_RendersOneRowPerBackupExistsEntry()
    {
        // Arrange
        _databaseService.Entries.Returns(
            [
                BuildEntry("a.db", backupExists: true),
                BuildEntry("b.db", backupExists: true),
                BuildEntry("c.db", backupExists: false)
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
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>();

        var rowCount = component.FindAll("li.recovery-row").Count;
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var deleteRadio = component.FindAll("li.recovery-row")[rowIndex]
                .QuerySelectorAll("input[type=radio]")[1];
            await deleteRadio.ChangeAsync(new() { Value = "on" });
        }

        // Act
        await component.Find("button.button:contains('Restore all')").ClickAsync(new());

        // Assert
        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)radios[0]).IsChecked);
            Assert.False(((AngleSharp.Html.Dom.IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_RowResolvedExternallyMidLoop_DoesNotCallServiceForResolvedRow()
    {
        // Arrange
        var entriesBefore = new DatabaseEntry[]
        {
            BuildEntry("a.db", backupExists: true),
            BuildEntry("b.db", backupExists: true)
        };

        var entriesAfter = new DatabaseEntry[]
        {
            BuildEntry("a.db", backupExists: true),
            BuildEntry("b.db", backupExists: false)
        };

        _databaseService.Entries.Returns(entriesBefore);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        _databaseService.Entries.Returns(entriesAfter);

        // Act
        await component.Find("button:contains('Apply')").ClickAsync(new());

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
            IsEnabled: false,
            DatabaseStatus.UpgradeRequired,
            BackupExists: backupExists);

    private static AngleSharp.Dom.IElement FindRowByFileName(
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

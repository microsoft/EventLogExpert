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
        var entriesBefore = new DatabaseEntry[]
        {
            BuildEntry("a.db", backupExists: true),
            BuildEntry("b.db", backupExists: true)
        };

        _databaseService.Entries.Returns(entriesBefore);

        // After Apply succeeds, the service raises EntriesChanged with both backups gone. Simulate
        // by flipping Entries to an empty list before raising the event from inside the call.
        _databaseService.RestoreFromBackupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        await component.Find("button:contains('Apply')").ClickAsync(new());

        // Now reflect the service raising EntriesChanged after the last successful Restore.
        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        await component.WaitForAssertionAsync(() => Assert.Equal(1, _onDismissedCallCount));
        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyDeleteReturnsFalse_SurfacesErrorBannerAndMarksRowFailed()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        await component.Find("button.button:contains('Delete all')").ClickAsync(new());
        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        var pendingRestore = new TaskCompletionSource<bool>();
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(pendingRestore.Task);

        var component = Render<DatabaseRecoveryDialog>();

        var applyClick = component.Find("button:contains('Apply')").ClickAsync(new());

        // While pending, every interactive control should be disabled.
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
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _databaseService.DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        // Row 1 stays Restore (default); flip row 2 to Delete by clicking its Delete radio.
        var bRow = component.FindAll("li.recovery-row")[1];
        var bDeleteRadio = bRow.QuerySelectorAll("input[type=radio]")[1];
        await bDeleteRadio.ChangeAsync(new() { Value = "on" });

        await component.Find("button:contains('Apply')").ClickAsync(new());

        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyRestoreReturnsFalse_SurfacesErrorBannerAndMarksRowFailed()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        // DatabaseService raises InvalidOperationException for "entry not found" / "operation in
        // progress". Both mean the world changed underneath us; the dialog must NOT surface a
        // recovery-failed banner because the user didn't actually lose anything.
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("entry not found")));

        var component = Render<DatabaseRecoveryDialog>();

        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new IOException("disk gone")));

        var component = Render<DatabaseRecoveryDialog>();

        await component.Find("button:contains('Apply')").ClickAsync(new());

        _bannerService.Received(1).ReportError(
            "Database recovery failed",
            "Failed to restore 'a.db' from backup.");

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_ApplyWithDelete_CallsDeleteEntryWithBackupAsyncWithFileName()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        await component.Find("button.button:contains('Delete all')").ClickAsync(new());
        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryDialog>();

        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        await component.Find("button:contains('Cancel')").ClickAsync(new());

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
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>();

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
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>();

        await component.Find("button.button:contains('Delete all')").ClickAsync(new());

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Flip the backing entries to empty before raising EntriesChanged.
        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        await component.WaitForAssertionAsync(() => Assert.Equal(1, _onDismissedCallCount));
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_EntriesChangedNewBackupExistsEntry_AddsRowWithRestoreDefault()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>();

        // A new entry appears (e.g. another database's upgrade just got interrupted).
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        component.WaitForState(() => component.FindAll("li.recovery-row").Count == 2);

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
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        // Flip "b.db" selection to Delete so we can prove the surviving "a.db" Restore selection is
        // preserved across the EntriesChanged refresh.
        var bRow = component.FindAll("li.recovery-row")[1];
        var bDeleteRadio = bRow.QuerySelectorAll("input[type=radio]")[1];
        await bDeleteRadio.ChangeAsync(new() { Value = "on" });

        // External resolution removes "b.db".
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        component.WaitForState(() => component.FindAll("li.recovery-row").Count == 1);

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
        _databaseService.Entries.Returns([BuildEntry("a.db", backupExists: true)]);
        var pendingRestore = new TaskCompletionSource<bool>();
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(pendingRestore.Task);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        var applyTask = component.Find("button:contains('Apply')").ClickAsync(new());

        // Esc on a <dialog> fires `cancel`. ModalChrome's @oncancel handler routes through
        // OnDialogClosedByUser, which we map to the same _isApplying-aware handler as the Cancel
        // button. So mid-Apply Esc must NOT invoke OnDismissed.
        await component.Find("dialog").TriggerEventAsync("oncancel", EventArgs.Empty);

        Assert.Equal(0, _onDismissedCallCount);

        pendingRestore.SetResult(true);
        await applyTask;
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_FailedRow_LosesFailureMarkOnNextApplySuccessWhileOthersStayFailed()
    {
        // Two rows both fail on first Apply.
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

        // Second Apply: a now succeeds, b still fails. Per-row failure-mark scoping means a loses
        // its mark but b keeps its mark.
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await component.Find("button:contains('Apply')").ClickAsync(new());

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
        // The dialog can be mounted after the parent decided recovery was needed but before our
        // first render — by which point every BackupExists entry may have already been resolved
        // (e.g. by another concurrent recovery path).
        _databaseService.Entries.Returns([]);

        var component = Render<DatabaseRecoveryDialog>(parameters => parameters
            .Add(p => p.OnDismissed, () => Interlocked.Increment(ref _onDismissedCallCount)));

        await component.WaitForAssertionAsync(() => Assert.Equal(1, _onDismissedCallCount));
    }

    [Fact]
    public void DatabaseRecoveryDialog_RendersOneRowPerBackupExistsEntry()
    {
        _databaseService.Entries.Returns(
            [
                BuildEntry("a.db", backupExists: true),
                BuildEntry("b.db", backupExists: true),
                BuildEntry("c.db", backupExists: false)
            ]);

        var component = Render<DatabaseRecoveryDialog>();

        var rows = component.FindAll("li.recovery-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("a.db", rows[0].TextContent);
        Assert.Contains("b.db", rows[1].TextContent);
    }

    [Fact]
    public async Task DatabaseRecoveryDialog_RestoreAllClicked_SetsAllRowsToRestore()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", backupExists: true), BuildEntry("b.db", backupExists: true)]);

        var component = Render<DatabaseRecoveryDialog>();

        // Flip both rows to Delete first so RestoreAll has something to flip back. Re-query inside
        // the loop because each ChangeAsync re-renders and invalidates prior element handles.
        var rowCount = component.FindAll("li.recovery-row").Count;
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var deleteRadio = component.FindAll("li.recovery-row")[rowIndex]
                .QuerySelectorAll("input[type=radio]")[1];
            await deleteRadio.ChangeAsync(new() { Value = "on" });
        }

        await component.Find("button.button:contains('Restore all')").ClickAsync(new());

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
        // Two rows in the snapshot the dialog originally rendered. Between OnInitialized and
        // ApplyAsync, an external code path resolves "b.db" — its BackupExists flips to false. The
        // dialog must re-check live state per row before calling the service, and silently skip "b.db".
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

        // Simulate the external resolution between render and Apply.
        _databaseService.Entries.Returns(entriesAfter);

        await component.Find("button:contains('Apply')").ClickAsync(new());

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

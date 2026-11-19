// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Text.RegularExpressions;
using TestContext = Xunit.TestContext;

namespace EventLogExpert.UI.Tests.Database;

public sealed class DatabaseRecoveryModalTests : BunitContext
{
    private static readonly TimeSpan s_handshakeTimeout = TimeSpan.FromSeconds(10);

    private readonly IDatabaseService _databaseService = Substitute.For<IDatabaseService>();
    private readonly IErrorBannerService _errorBannerService = Substitute.For<IErrorBannerService>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ModalId _modalId = new(1L);
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    private ModalRegistration? _capturedRegistration;

    public DatabaseRecoveryModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());

        _databaseService.Entries.Returns([]);
        _modalService.ActiveModalId.Returns(_modalId);

        _modalCoordinator
            .When(coordinator => coordinator.RegisterModal(Arg.Any<ModalRegistration>()))
            .Do(call => _capturedRegistration = call.Arg<ModalRegistration>());

        Services.AddSingleton(_errorBannerService);
        Services.AddSingleton(_databaseService);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task DatabaseRecoveryModal_AllRowsSucceed_AutoCompletesWithFalseWhenEntriesDrain()
    {
        var entriesBefore = new[]
        {
            BuildEntry("a.db", true),
            BuildEntry("b.db", true)
        };

        _databaseService.Entries.Returns(entriesBefore);

        _databaseService.RestoreFromBackupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseRestoreResult.Restored));

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        await component.WaitForAssertionAsync(() =>
            _modalService.Received().Complete(_modalId, Arg.Is<object?>(value => Equals(value, false))));
        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyDeleteReturnsFalse_SurfacesErrorBannerAndMarksRowFailed()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button.button:contains('[[DatabaseRecoveryModal_DeleteAll]]')").ClickAsync(new MouseEventArgs());
        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        _errorBannerService.Received(1).ReportError(
            Arg.Is<BannerMessage>(message =>
                IsPreformatted(message, "[[Banner_Recovery_Failed_Title]]", "[[Banner_Recovery_Failed_Delete(a.db)]]")));

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
        _modalService.DidNotReceive().Complete(Arg.Any<ModalId>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyMixed_CallsBothMethodsForRespectiveRows()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseRestoreResult.Restored));
        _databaseService.DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryModal>();

        var bRow = component.FindAll("li.recovery-row")[1];
        var bDeleteRadio = bRow.QuerySelectorAll("input[type=radio]")[1];
        await bDeleteRadio.ChangeAsync(new ChangeEventArgs { Value = "on" });

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyPending_BatchButtonCallbacks_LeaveSelectionsUnchanged()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCompletion = new TaskCompletionSource<DatabaseRestoreResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreStarted.TrySetResult();
                return restoreCompletion.Task;
            });
        _databaseService.DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryModal>();
        await FindDeleteRadio(component, "b.db").ChangeAsync(new ChangeEventArgs { Value = "on" });

        Task applyTask = component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());
        await restoreStarted.Task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

        await component.InvokeAsync(() =>
            FindBatchButton(component, "DatabaseRecoveryModal_DeleteAll").Instance.OnClick
                .InvokeAsync(new MouseEventArgs()));

        Assert.True(FindRestoreRadio(component, "a.db").IsChecked);
        Assert.True(FindDeleteRadio(component, "b.db").IsChecked);

        await component.InvokeAsync(() =>
            FindBatchButton(component, "DatabaseRecoveryModal_RestoreAll").Instance.OnClick
                .InvokeAsync(new MouseEventArgs()));

        Assert.True(FindRestoreRadio(component, "a.db").IsChecked);
        Assert.True(FindDeleteRadio(component, "b.db").IsChecked);
        Assert.True(((IHtmlButtonElement)component.Find("button.button:contains('[[DatabaseRecoveryModal_RestoreAll]]')")).IsDisabled);
        Assert.True(((IHtmlButtonElement)component.Find("button.button:contains('[[DatabaseRecoveryModal_DeleteAll]]')")).IsDisabled);

        restoreCompletion.SetResult(DatabaseRestoreResult.Restored);
        await applyTask;
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyPending_DisablesButtonsAndKeepsRadiosEnabled()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        var pendingRestore = new TaskCompletionSource<DatabaseRestoreResult>();
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(pendingRestore.Task);

        var component = Render<DatabaseRecoveryModal>();

        var applyClick = component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        Assert.True(((IHtmlButtonElement)component.Find("button:contains('[[Modal_Apply]]')")).IsDisabled);
        Assert.True(((IHtmlButtonElement)component.Find("button:contains('[[Modal_Cancel]]')")).IsDisabled);
        Assert.True(((IHtmlButtonElement)component.Find("button.button:contains('[[DatabaseRecoveryModal_RestoreAll]]')")).IsDisabled);
        Assert.True(((IHtmlButtonElement)component.Find("button.button:contains('[[DatabaseRecoveryModal_DeleteAll]]')")).IsDisabled);

        foreach (var radio in component.FindAll("li.recovery-row input[type=radio]"))
        {
            Assert.False(((IHtmlInputElement)radio).IsDisabled);
        }

        pendingRestore.SetResult(DatabaseRestoreResult.Restored);
        await applyClick;
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyPending_QueuedRadioChange_AppliesOnNextApply()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCompletion = new TaskCompletionSource<DatabaseRestoreResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreStarted.TrySetResult();
                return restoreCompletion.Task;
            });
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryModal>();

        Task firstApply = component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());
        await restoreStarted.Task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

        await FindDeleteRadio(component, "a.db").ChangeAsync(new ChangeEventArgs { Value = "on" });

        restoreCompletion.SetResult(DatabaseRestoreResult.Failed);
        await firstApply;

        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>());

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyPending_QueuedSelectionsForChangedRows_AreDroppedAndOthersKept()
    {
        DatabaseEntry entryA = BuildEntry("a.db", true);
        DatabaseEntry entryB = BuildEntry("b.db", true);
        _databaseService.Entries.Returns([entryA, entryB]);

        var restoreAStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreACompletion = new TaskCompletionSource<DatabaseRestoreResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreBStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreBCompletion = new TaskCompletionSource<DatabaseRestoreResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreAStarted.TrySetResult();
                return restoreACompletion.Task;
            });
        _databaseService.RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreBStarted.TrySetResult();
                return restoreBCompletion.Task;
            });
        _databaseService.DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryModal>();

        Task firstApply = component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());
        await restoreAStarted.Task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

        await FindDeleteRadio(component, "a.db").ChangeAsync(new ChangeEventArgs { Value = "on" });
        await FindDeleteRadio(component, "b.db").ChangeAsync(new ChangeEventArgs { Value = "on" });

        _databaseService.Entries.Returns([entryB]);
        restoreACompletion.SetResult(DatabaseRestoreResult.Restored);
        await restoreBStarted.Task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);
        restoreBCompletion.SetResult(DatabaseRestoreResult.Failed);
        await firstApply;

        Assert.Single(component.FindAll("li.recovery-row"));
        Assert.True(FindDeleteRadio(component, "b.db").IsChecked);

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(1).DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>());

        _databaseService.Entries.Returns([entryA, entryB]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);
        await component.InvokeAsync(() => { });

        Assert.True(FindRestoreRadio(component, "a.db").IsChecked);
        Assert.False(FindDeleteRadio(component, "a.db").IsChecked);
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyPending_ShowsQueuedSelectionNoteLinkedToRadios()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCompletion = new TaskCompletionSource<DatabaseRestoreResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _databaseService.RestoreFromBackupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreStarted.TrySetResult();
                return restoreCompletion.Task;
            });

        var component = Render<DatabaseRecoveryModal>();

        Assert.Empty(component.Find("#recovery-queued-note").TextContent);
        Assert.All(
            component.FindAll("li.recovery-row input[type=radio]"),
            radio => Assert.False(radio.HasAttribute("aria-describedby")));

        Task applyTask = component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());
        await restoreStarted.Task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

        var note = component.Find("#recovery-queued-note");
        Assert.Equal("status", note.GetAttribute("role"));
        Assert.Equal("[[DatabaseRecoveryModal_QueuedSelectionNote]]", note.TextContent.Trim());
        Assert.Equal(4, component.FindAll("li.recovery-row input[type=radio]").Count);
        Assert.All(
            component.FindAll("li.recovery-row input[type=radio]"),
            radio => Assert.Equal("recovery-queued-note", radio.GetAttribute("aria-describedby")));

        restoreCompletion.SetResult(DatabaseRestoreResult.Restored);
        await applyTask;

        Assert.Empty(component.Find("#recovery-queued-note").TextContent);
        Assert.All(
            component.FindAll("li.recovery-row input[type=radio]"),
            radio => Assert.False(radio.HasAttribute("aria-describedby")));
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyRestoreReturnsFailed_SurfacesErrorBannerAndMarksRowFailed()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseRestoreResult.Failed));

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        _errorBannerService.Received(1).ReportError(
            Arg.Is<BannerMessage>(message =>
                IsPreformatted(message, "[[Banner_Recovery_Failed_Title]]", "[[Banner_Recovery_Failed_Restore(a.db)]]")));

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
        _modalService.DidNotReceive().Complete(Arg.Any<ModalId>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyRestoreReturnsRestoredBackupRetained_DropsQueuedDeleteAndReportsRetainedBanner()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCompletion = new TaskCompletionSource<DatabaseRestoreResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    restoreStarted.TrySetResult();
                    return restoreCompletion.Task;
                },
                _ => Task.FromResult(DatabaseRestoreResult.Restored));
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryModal>();

        Task firstApply = component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());
        await restoreStarted.Task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

        await FindDeleteRadio(component, "a.db").ChangeAsync(new ChangeEventArgs { Value = "on" });
        Assert.True(FindDeleteRadio(component, "a.db").IsChecked);

        restoreCompletion.SetResult(DatabaseRestoreResult.RestoredBackupRetained);
        await firstApply;

        _errorBannerService.Received(1).ReportError(Arg.Any<BannerMessage>());
        _errorBannerService.Received(1).ReportError(
            Arg.Is<BannerMessage>(message =>
                IsPreformatted(
                    message,
                    "[[Banner_Recovery_BackupRetained_Title]]",
                    "[[Banner_Recovery_BackupRetained(a.db)]]")));
        Assert.Contains("recovery-row-failed", component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty);
        Assert.True(FindRestoreRadio(component, "a.db").IsChecked);
        Assert.False(FindDeleteRadio(component, "a.db").IsChecked);

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyRestoreReturnsRestoredWhileRowStaysListed_DropsQueuedDelete()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCompletion = new TaskCompletionSource<DatabaseRestoreResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    restoreStarted.TrySetResult();
                    return restoreCompletion.Task;
                },
                _ => Task.FromResult(DatabaseRestoreResult.Restored));
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryModal>();

        Task firstApply = component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());
        await restoreStarted.Task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

        await FindDeleteRadio(component, "a.db").ChangeAsync(new ChangeEventArgs { Value = "on" });
        Assert.True(FindDeleteRadio(component, "a.db").IsChecked);

        restoreCompletion.SetResult(DatabaseRestoreResult.Restored);
        await firstApply;

        Assert.True(FindRestoreRadio(component, "a.db").IsChecked);
        Assert.False(FindDeleteRadio(component, "a.db").IsChecked);

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.Received(2).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyThrowsInvalidOperation_TreatsAsBenignSkipNoErrorBanner()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DatabaseRestoreResult>(new InvalidOperationException("entry not found")));

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        _errorBannerService.DidNotReceive().ReportError(Arg.Any<BannerMessage>());

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.DoesNotContain("recovery-row-failed", rowClass);
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyThrowsUnexpected_SurfacesErrorBannerAndMarksRowFailed()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DatabaseRestoreResult>(new IOException("disk gone")));

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        _errorBannerService.Received(1).ReportError(
            Arg.Is<BannerMessage>(message =>
                IsPreformatted(message, "[[Banner_Recovery_Failed_Title]]", "[[Banner_Recovery_Failed_Restore(a.db)]]")));

        var rowClass = component.Find("li.recovery-row").GetAttribute("class") ?? string.Empty;
        Assert.Contains("recovery-row-failed", rowClass);
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyUsesActionsSelectedAtStartWhenEntriesChange()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        var restoreStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCompletion = new TaskCompletionSource<DatabaseRestoreResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreStarted.TrySetResult(true);
                return restoreCompletion.Task;
            });
        _databaseService.DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryModal>();
        var bRow = component.FindAll("li.recovery-row")[1];
        await bRow.QuerySelectorAll("input[type=radio]")[1]
            .ChangeAsync(new ChangeEventArgs { Value = "on" });

        Task applyTask = component.Find("button:contains('[[Modal_Apply]]')")
            .ClickAsync(new MouseEventArgs());
        await restoreStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);
        await component.InvokeAsync(() => Task.CompletedTask);

        _databaseService.Entries.Returns([BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);
        await component.InvokeAsync(() => Task.CompletedTask);

        restoreCompletion.SetResult(DatabaseRestoreResult.Restored);
        await applyTask;

        await _databaseService.Received(1).DeleteEntryWithBackupAsync("b.db", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyWithDelete_CallsDeleteEntryWithBackupAsyncWithFileName()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.DeleteEntryWithBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button.button:contains('[[DatabaseRecoveryModal_DeleteAll]]')").ClickAsync(new MouseEventArgs());
        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        await _databaseService.Received(1).DeleteEntryWithBackupAsync(
            Arg.Is<string>(name => name == "a.db"),
            Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().RestoreFromBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_ApplyWithRestore_CallsRestoreFromBackupAsyncWithFileName()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseRestoreResult.Restored));

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        await _databaseService.Received(1).RestoreFromBackupAsync(
            Arg.Is<string>(name => name == "a.db"),
            Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_CancelClicked_RoutesThroughCoordinatorAndDoesNotCallDatabaseService()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button:contains('[[Modal_Cancel]]')").ClickAsync(new MouseEventArgs());

        await _modalCoordinator.Received(1).RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        await _databaseService.DidNotReceive().RestoreFromBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().DeleteEntryWithBackupAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DatabaseRecoveryModal_DefaultsEachRowToRestore()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.Equal(2, radios.Length);
            Assert.True(((IHtmlInputElement)radios[0]).IsChecked);
            Assert.False(((IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Fact]
    public async Task DatabaseRecoveryModal_DeleteAllClicked_SetsAllRowsToDelete()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button.button:contains('[[DatabaseRecoveryModal_DeleteAll]]')").ClickAsync(new MouseEventArgs());

        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.False(((IHtmlInputElement)radios[0]).IsChecked);
            Assert.True(((IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Theory]
    [InlineData(1, "[[DatabaseRecoveryModal_Description]] [[DatabaseRecoveryModal_Explanation]]")]
    [InlineData(2, "[[DatabaseRecoveryModal_Description]] [[DatabaseRecoveryModal_Explanation]]")]
    public void DatabaseRecoveryModal_Description_RendersSentencesSeparatedBySingleSpace(int entryCount, string expected)
    {
        var entries = new DatabaseEntry[entryCount];

        for (int index = 0; index < entryCount; index++)
        {
            entries[index] = BuildEntry($"db{index}.db", true);
        }

        _databaseService.Entries.Returns(entries);

        var component = Render<DatabaseRecoveryModal>();

        string text = Regex.Replace(
            component.Find("p.recovery-description").TextContent,
            @"\s+",
            " ").Trim();

        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task DatabaseRecoveryModal_EntriesChangedAllResolved_AutoCompletesWithFalse()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        _databaseService.Entries.Returns([]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        await component.WaitForAssertionAsync(() =>
            _modalService.Received().Complete(_modalId, Arg.Is<object?>(value => Equals(value, false))));
    }

    [Fact]
    public async Task DatabaseRecoveryModal_EntriesChangedNewBackupExistsEntry_AddsRowWithRestoreDefault()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        component.WaitForAssertion(() =>
        {
            var rows = component.FindAll("li.recovery-row");
            Assert.Equal(2, rows.Count);

            var newRow = rows[1];
            Assert.Contains("b.db", newRow.TextContent);

            var radios = newRow.QuerySelectorAll("input[type=radio]");
            Assert.True(((IHtmlInputElement)radios[0]).IsChecked);
            Assert.False(((IHtmlInputElement)radios[1]).IsChecked);
        });

        await Task.CompletedTask;
    }

    [Fact]
    public async Task DatabaseRecoveryModal_EntriesChangedQueuedBeforeDispose_DoesNotReadEntriesOrCompleteAgain()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        _databaseService.Entries.Returns([]);
        _databaseService.ClearReceivedCalls();
        _modalService.ClearReceivedCalls();

        using ManualResetEventSlim eventRaised = new(false);
        Task? foreignRaise = null;

        await component.InvokeAsync(async () =>
        {
            foreignRaise = Task.Run(
                () =>
                {
                    _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);
                    eventRaised.Set();
                },
                TestContext.Current.CancellationToken);

            Assert.True(eventRaised.Wait(s_handshakeTimeout, TestContext.Current.CancellationToken));

            await component.Instance.DisposeAsync();
        });

        await foreignRaise!;
        await component.InvokeAsync(() => { });

        _ = _databaseService.DidNotReceive().Entries;
        _modalService.Received(1).Complete(_modalId, Arg.Any<object?>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_EntriesChangedSubsetResolved_RemovesResolvedRowsKeepsDialogOpen()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        var bRow = component.FindAll("li.recovery-row")[1];
        var bDeleteRadio = bRow.QuerySelectorAll("input[type=radio]")[1];
        await bDeleteRadio.ChangeAsync(new ChangeEventArgs { Value = "on" });

        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        _databaseService.EntriesChanged += Raise.Event<EventHandler>(_databaseService, EventArgs.Empty);

        component.WaitForAssertion(() =>
        {
            var remainingRows = component.FindAll("li.recovery-row");
            Assert.Single(remainingRows);

            var remainingRow = remainingRows[0];
            Assert.Contains("a.db", remainingRow.TextContent);

            var radios = remainingRow.QuerySelectorAll("input[type=radio]");
            Assert.True(((IHtmlInputElement)radios[0]).IsChecked);
            Assert.False(((IHtmlInputElement)radios[1]).IsChecked);
        });

        _modalService.DidNotReceive().Complete(Arg.Any<ModalId>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_FailedRow_LosesFailureMarkOnNextApplySuccessWhileOthersStayFailed()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseRestoreResult.Failed));
        _databaseService.RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseRestoreResult.Failed));

        var component = Render<DatabaseRecoveryModal>();

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "a.db").GetAttribute("class") ?? string.Empty);
        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "b.db").GetAttribute("class") ?? string.Empty);

        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseRestoreResult.Restored));

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        Assert.DoesNotContain(
            "recovery-row-failed",
            FindRowByFileName(component, "a.db").GetAttribute("class") ?? string.Empty);
        Assert.Contains(
            "recovery-row-failed",
            FindRowByFileName(component, "b.db").GetAttribute("class") ?? string.Empty);
    }

    [Fact]
    public async Task DatabaseRecoveryModal_InitialEmptySet_AutoCompletesWithoutShowingModal()
    {
        _databaseService.Entries.Returns([]);

        var component = Render<DatabaseRecoveryModal>();

        await component.WaitForAssertionAsync(() =>
            _modalService.Received().Complete(_modalId, Arg.Is<object?>(value => Equals(value, false))));
    }

    [Fact]
    public async Task DatabaseRecoveryModal_OnRequestCloseAsync_WhenIdle_ReturnsTrue()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        Render<DatabaseRecoveryModal>();
        Assert.NotNull(_capturedRegistration);

        bool accepted = await _capturedRegistration!.RequestClose(
            new ModalCloseRequest(ModalCloseReason.UserDismiss));

        Assert.True(accepted);
    }

    [Fact]
    public async Task DatabaseRecoveryModal_OnRequestCloseAsync_WhileApplying_ReturnsFalse()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);
        var pendingRestore = new TaskCompletionSource<DatabaseRestoreResult>();
        _databaseService.RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>())
            .Returns(pendingRestore.Task);

        var component = Render<DatabaseRecoveryModal>();
        Assert.NotNull(_capturedRegistration);

        var applyClick = component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        bool accepted = await _capturedRegistration!.RequestClose(
            new ModalCloseRequest(ModalCloseReason.UserDismiss));

        Assert.False(accepted);

        pendingRestore.SetResult(DatabaseRestoreResult.Restored);
        await applyClick;
    }

    [Fact]
    public void DatabaseRecoveryModal_RegistersAsCriticalScope()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        Render<DatabaseRecoveryModal>();

        Assert.NotNull(_capturedRegistration);
        Assert.Equal(ModalScope.Critical, _capturedRegistration!.Scope);
    }

    [Fact]
    public void DatabaseRecoveryModal_RendersOneRowPerBackupExistsEntry()
    {
        _databaseService.Entries.Returns(
            [
                BuildEntry("a.db", true),
                BuildEntry("b.db", true),
                BuildEntry("c.db", false)
            ]);

        var component = Render<DatabaseRecoveryModal>();

        var rows = component.FindAll("li.recovery-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("a.db", rows[0].TextContent);
        Assert.Contains("b.db", rows[1].TextContent);
    }

    [Fact]
    public async Task DatabaseRecoveryModal_RestoreAllClicked_SetsAllRowsToRestore()
    {
        _databaseService.Entries.Returns(
            [BuildEntry("a.db", true), BuildEntry("b.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        var rowCount = component.FindAll("li.recovery-row").Count;
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var deleteRadio = component.FindAll("li.recovery-row")[rowIndex]
                .QuerySelectorAll("input[type=radio]")[1];
            await deleteRadio.ChangeAsync(new ChangeEventArgs { Value = "on" });
        }

        await component.Find("button.button:contains('[[DatabaseRecoveryModal_RestoreAll]]')").ClickAsync(new MouseEventArgs());

        foreach (var row in component.FindAll("li.recovery-row"))
        {
            var radios = row.QuerySelectorAll("input[type=radio]");
            Assert.True(((IHtmlInputElement)radios[0]).IsChecked);
            Assert.False(((IHtmlInputElement)radios[1]).IsChecked);
        }
    }

    [Fact]
    public async Task DatabaseRecoveryModal_RowResolvedExternallyMidLoop_DoesNotCallServiceForResolvedRow()
    {
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
            .Returns(Task.FromResult(DatabaseRestoreResult.Restored));

        var component = Render<DatabaseRecoveryModal>();

        _databaseService.Entries.Returns(entriesAfter);

        await component.Find("button:contains('[[Modal_Apply]]')").ClickAsync(new MouseEventArgs());

        await _databaseService.Received(1).RestoreFromBackupAsync("a.db", Arg.Any<CancellationToken>());
        await _databaseService.DidNotReceive().RestoreFromBackupAsync("b.db", Arg.Any<CancellationToken>());
        _errorBannerService.DidNotReceive().ReportError(Arg.Any<BannerMessage>());
    }

    [Fact]
    public async Task DatabaseRecoveryModal_WhenInlineAlertRequested_ResolvesViaInlineAlertHost()
    {
        _databaseService.Entries.Returns([BuildEntry("a.db", true)]);

        var component = Render<DatabaseRecoveryModal>();

        var alertTask = component.Instance.ShowInlineAlertAsync(
            new InlineAlertRequest(
                Title: "Test",
                Message: "Test message",
                AcceptLabel: "OK",
                CancelLabel: "Cancel",
                IsPrompt: false,
                PromptInitialValue: null),
            CancellationToken.None);

        var acceptButton = await component.WaitForElementAsync(".inline-alert-buttons button.button-green");

        await acceptButton.ClickAsync(new MouseEventArgs());

        InlineAlertResult result = await alertTask;
        Assert.True(result.Accepted);
    }

    private static DatabaseEntry BuildEntry(string fileName, bool backupExists) =>
        new(
            fileName,
            $@"C:\dbs\{fileName}",
            false,
            DatabaseStatus.UpgradeRequired,
            backupExists);

    private static IRenderedComponent<Button> FindBatchButton(
        IRenderedComponent<DatabaseRecoveryModal> component,
        string labelKey) =>
        component.FindComponents<Button>().Single(button =>
            button.Find("button").TextContent.Contains($"[[{labelKey}]]", StringComparison.Ordinal));

    private static IHtmlInputElement FindDeleteRadio(
        IRenderedComponent<DatabaseRecoveryModal> component,
        string fileName) =>
        (IHtmlInputElement)FindRowByFileName(component, fileName).QuerySelectorAll("input[type=radio]")[1];

    private static IHtmlInputElement FindRestoreRadio(
        IRenderedComponent<DatabaseRecoveryModal> component,
        string fileName) =>
        (IHtmlInputElement)FindRowByFileName(component, fileName).QuerySelectorAll("input[type=radio]")[0];

    private static IElement FindRowByFileName(
        IRenderedComponent<DatabaseRecoveryModal> component,
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

    private static bool IsPreformatted(BannerMessage? message, string title, string text, string? actionLabel = null) =>
        message is Preformatted preformatted &&
        preformatted.Title == title &&
        preformatted.Message == text &&
        preformatted.ActionLabel == actionLabel;
}

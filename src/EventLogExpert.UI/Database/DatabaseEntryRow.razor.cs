// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Database;

public sealed partial class DatabaseEntryRow : ComponentBase, IAsyncDisposable
{
    private readonly string _nameButtonId = $"db-row-{Guid.NewGuid():N}-name";
    private readonly string _pendingStatusId = $"db-row-{Guid.NewGuid():N}-pending";

    private ElementReference _checkboxRef;
    private bool _disposed;
    private IJSObjectReference? _jsModule;
    private ElementReference _nameButtonRef;
    private ElementReference _removeButtonRef;
    private bool _shouldFocusNameAfterRender;

    private enum ActionKind
    {
        None,
        Toggle,
        DisabledToggle,
        Upgrade,
        Retry,
        Spinner,
        RestoreFromBackup,
        RetryClassification
    }

    [Parameter] public bool EffectiveEnabled { get; set; }

    [Parameter] public required DatabaseEntry Entry { get; set; }

    [Parameter] public bool IsClassificationPending { get; set; }

    [Parameter] public bool IsSelected { get; set; }

    [Parameter] public bool IsTogglePending { get; set; }

    [Parameter] public bool IsUpgradeBlocked { get; set; }

    [Parameter] public bool IsUpgrading { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnRestoreFromBackup { get; set; }

    [Parameter] public EventCallback OnRetryClassification { get; set; }

    [Parameter] public EventCallback OnSelectionToggle { get; set; }

    [Parameter] public EventCallback OnToggle { get; set; }

    [Parameter] public EventCallback OnUpgrade { get; set; }

    [Parameter] public BannerProgressEntry? UpgradeProgress { get; set; }

    private string BadgeKind => Entry.BackupExists ? "Recovery" : Entry.Status.ToString();

    private string BadgeLabel => DatabaseStatusLabels.GetRowBadgeLabel(Entry);

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    private bool IsRestoreBlocked => IsUpgradeBlocked || IsUpgrading || UpgradeProgress is not null;

    private ActionKind PrimaryAction
    {
        get
        {
            if (Entry.BackupExists) { return ActionKind.RestoreFromBackup; }

            if (IsUpgrading || UpgradeProgress is not null) { return ActionKind.Spinner; }

            return Entry.Status switch
            {
                DatabaseStatus.Ready =>
                    IsClassificationPending ? ActionKind.DisabledToggle : ActionKind.Toggle,
                DatabaseStatus.NotClassified => ActionKind.DisabledToggle,
                DatabaseStatus.UpgradeRequired => ActionKind.Upgrade,
                DatabaseStatus.UpgradeFailed => ActionKind.Retry,
                DatabaseStatus.UnrecognizedSchema => ActionKind.None,
                DatabaseStatus.ObsoleteSchema => ActionKind.None,
                DatabaseStatus.ClassificationFailed => ActionKind.RetryClassification,
                _ => ActionKind.None
            };
        }
    }

    private bool ShouldShowBadge => Entry.BackupExists ||
        (!IsUpgrading &&
            UpgradeProgress is null &&
            Entry.Status != DatabaseStatus.Ready &&
            Entry.Status != DatabaseStatus.UpgradeRequired);

    private bool ShowPendingIndicator => IsTogglePending && PrimaryAction != ActionKind.DisabledToggle;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;

        if (_jsModule is not null)
        {
            try { await _jsModule.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
            _jsModule = null;
        }
    }

    public ValueTask FocusRemoveButtonAsync() => _removeButtonRef.FocusAsync(preventScroll: true);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/Database/DatabaseEntryRow.js");
                await _jsModule.InvokeVoidAsync("attachCheckboxKeyHandler", _checkboxRef);
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }

        if (!_shouldFocusNameAfterRender) { return; }

        _shouldFocusNameAfterRender = false;

        try { await _nameButtonRef.FocusAsync(preventScroll: true); }
        catch (ObjectDisposedException) { }
        catch (JSException) { }
    }

    private static string PhaseVerb(UpgradePhase phase) => phase switch
    {
        UpgradePhase.BackingUp => "Backing up",
        UpgradePhase.MigratingSchema => "Migrating schema",
        UpgradePhase.Verifying => "Verifying",
        _ => "Upgrading"
    };

    private async Task HandleCheckboxClick() => await OnSelectionToggle.InvokeAsync();

    private async Task HandleCheckboxKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is " " or "Spacebar" or "Enter")
        {
            await OnSelectionToggle.InvokeAsync();
        }
    }

    private void OnCancelClick()
    {
        _shouldFocusNameAfterRender = true;

        try { UpgradeProgress?.Cancel(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TraceLogger.Warning(
                $"{nameof(DatabaseEntryRow)}.{nameof(OnCancelClick)}: cancel threw: {ex}");
        }
    }

    private async Task OnRemoveClick() => await OnRemove.InvokeAsync();
}

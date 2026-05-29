// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Database;

public sealed partial class DatabaseEntryRow : ComponentBase
{
    private readonly string _nameButtonId = $"db-row-{Guid.NewGuid():N}-name";
    private readonly string _pendingStatusId = $"db-row-{Guid.NewGuid():N}-pending";

    private bool _isMouseRevealed;
    private ElementReference _nameButtonRef;
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

    [Parameter] public bool IsTogglePending { get; set; }

    [Parameter] public bool IsUpgradeBlocked { get; set; }

    [Parameter] public bool IsUpgrading { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnRestoreFromBackup { get; set; }

    [Parameter] public EventCallback OnRetryClassification { get; set; }

    [Parameter] public EventCallback OnToggle { get; set; }

    [Parameter] public EventCallback OnUpgrade { get; set; }

    [Parameter] public BannerProgressEntry? UpgradeProgress { get; set; }

    private string BadgeKind => Entry.BackupExists ? "Recovery" : Entry.Status.ToString();

    private string BadgeLabel => DatabaseStatusLabels.GetRowBadgeLabel(Entry);

    private bool IsRemoveBlocked => IsUpgrading || UpgradeProgress is not null;

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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
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

    private void HandleNameClick() => _isMouseRevealed = true;

    private void HandleRowMouseLeave() => _isMouseRevealed = false;

    private void OnCancelClick()
    {
        _shouldFocusNameAfterRender = true;
        UpgradeProgress?.Cancel();
    }

    private void OnRemoveClick()
    {
        if (IsRemoveBlocked) { return; }
        OnRemove.InvokeAsync();
    }
}

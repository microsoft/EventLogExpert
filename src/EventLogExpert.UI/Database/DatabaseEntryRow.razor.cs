// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.Menu;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Database;

public sealed partial class DatabaseEntryRow : ComponentBase
{
    private static readonly IReadOnlyDictionary<string, object> s_ariaHiddenTrueAttributes =
        new Dictionary<string, object>(StringComparer.Ordinal) { ["aria-hidden"] = "true" };

    private readonly string _nameButtonId = $"db-row-{Guid.NewGuid():N}-name";
    private readonly string _pendingStatusId = $"db-row-{Guid.NewGuid():N}-pending";

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

    [Parameter] public bool IsSelected { get; set; }

    [Parameter] public bool IsSelectionModeActive { get; set; }

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

    private IReadOnlyDictionary<string, object>? CheckboxHiddenAttributes =>
        IsSelectionModeActive ? null : s_ariaHiddenTrueAttributes;

    private bool IsRestoreBlocked => IsUpgradeBlocked || IsUpgrading || UpgradeProgress is not null;

    [Inject] private IMenuService MenuService { get; init; } = null!;

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

    public ValueTask FocusNameAsync() => _nameButtonRef.FocusAsync(preventScroll: true);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_shouldFocusNameAfterRender) { return; }

        _shouldFocusNameAfterRender = false;

        try { await _nameButtonRef.FocusAsync(preventScroll: true); }
        catch (ObjectDisposedException) { }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }

    private static string PhaseVerb(UpgradePhase phase) => phase switch
    {
        UpgradePhase.BackingUp => "Backing up",
        UpgradePhase.MigratingSchema => "Migrating schema",
        UpgradePhase.Verifying => "Verifying",
        _ => "Upgrading"
    };

    private void HandleContextMenu(MouseEventArgs args)
    {
        // Suppressed in selection mode — bulk strip is the action surface.
        if (IsSelectionModeActive) { return; }

        var items = new List<MenuItem>
        {
            MenuItem.Item("Remove", () => OnRemove.InvokeAsync()),
        };

        MenuService.OpenAt(args.ClientX, args.ClientY, items);
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
}

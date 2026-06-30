// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Globalization;

namespace EventLogExpert.UI.Database;

public sealed partial class DatabaseEntryRow : ComponentBase
{
    // Distinct OS versions shown before collapsing to "N+". DatabaseClassificationService reads one MORE than this (the
    // cap + 1), so a meaningful count exceeding the cap means "more than cap distinct versions", rendered as "cap+".
    private const int OsStampDisplayCap = 9;

    private static readonly IReadOnlyDictionary<string, object> s_ariaHiddenTrueAttributes =
        new Dictionary<string, object>(StringComparer.Ordinal) { ["aria-hidden"] = "true" };

    private readonly string _nameButtonId = ComponentId.NewUnique("db-row-name").Value;
    private readonly string _pendingStatusId = ComponentId.NewUnique("db-row-pending").Value;

    private ChromelessButton? _nameButton;
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

    private bool HasOsStamp => MeaningfulOsStamps.Count > 0;

    private bool IsRestoreBlocked => IsUpgradeBlocked || IsUpgrading || UpgradeProgress is not null;

    // The distinct OS stamps worth displaying. A legacy/unstamped row - all fields null, or whose only numeric signal is a
    // non-positive build (0 is never a real build) or a bare revision - carries no real provenance and is dropped.
    private IReadOnlyList<ProviderDatabaseOsStamp> MeaningfulOsStamps =>
        Entry.OsStamps.Where(HasAnyField).ToList();

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private string OsStampAriaLabel => $"Source OS: {OsStampDetail}";

    // Full list (the hover title AND the accessible label) so the OS info is never hover-only.
    private string OsStampDetail => string.Join("; ", MeaningfulOsStamps.Select(FormatStamp));

    // Compact visible indicator: one stamp formatted, or "Mixed (N OS versions)" for a merged database.
    private string OsStampSummary
    {
        get
        {
            var stamps = MeaningfulOsStamps;

            if (stamps.Count == 0) { return string.Empty; }

            if (stamps.Count == 1) { return FormatStamp(stamps[0]); }

            return stamps.Count > OsStampDisplayCap
                ? $"Mixed ({OsStampDisplayCap}+ OS versions)"
                : $"Mixed ({stamps.Count} OS versions)";
        }
    }

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

    public ValueTask FocusNameAsync() =>
        _nameButton is { } button ?
            ElementFocus.SafelyAsync(button.Element, preventScroll: true) :
            ValueTask.CompletedTask;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_shouldFocusNameAfterRender) { return; }

        _shouldFocusNameAfterRender = false;

        await FocusNameAsync();
    }

    private static string? FormatBuildRevision(int? build, int? revision)
    {
        // A build of 0 / negative is never a real Windows build, and a revision (UBR) is not meaningful without its build,
        // so render the segment only for a valid (> 0) build. A revision of 0 IS valid for a real build (e.g. an RTM UBR).
        if (build is not > 0) { return null; }

        return revision is not null
            ? string.Create(CultureInfo.InvariantCulture, $"{build.Value}.{revision.Value}")
            : build.Value.ToString(CultureInfo.InvariantCulture);
    }

    // "Edition · DisplayVersion · Build.Revision" with null segments omitted; raw values, no marketing-name mapping.
    private static string FormatStamp(ProviderDatabaseOsStamp stamp)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrEmpty(stamp.Edition)) { parts.Add(stamp.Edition); }

        if (!string.IsNullOrEmpty(stamp.DisplayVersion)) { parts.Add(stamp.DisplayVersion); }

        if (FormatBuildRevision(stamp.Build, stamp.Revision) is { } buildRevision) { parts.Add(buildRevision); }

        return parts.Count > 0 ? string.Join(" \u00B7 ", parts) : "Unknown OS";
    }

    private static bool HasAnyField(ProviderDatabaseOsStamp stamp) =>
        stamp.Build is > 0 ||
        !string.IsNullOrEmpty(stamp.Edition) ||
        !string.IsNullOrEmpty(stamp.DisplayVersion);

    private static string PhaseVerb(UpgradePhase phase) => phase switch
    {
        UpgradePhase.BackingUp => "Backing up",
        UpgradePhase.MigratingSchema => "Migrating schema",
        UpgradePhase.Verifying => "Verifying",
        _ => "Upgrading"
    };

    private void HandleContextMenu(MouseEventArgs args)
    {
        // Suppressed in selection mode - bulk strip is the action surface.
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

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.Menu;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Database;

public sealed partial class DatabaseEntryRow : ComponentBase
{
    private const int OsStampDisplayCap = 9;

    private static readonly IReadOnlyDictionary<string, object> s_ariaHiddenTrueAttributes =
        new Dictionary<string, object>(StringComparer.Ordinal) { ["aria-hidden"] = "true" };

    private readonly string _nameButtonId = ComponentId.NewUnique("db-row-name").Value;
    private readonly string _pendingStatusId = ComponentId.NewUnique("db-row-pending").Value;

    private IReadOnlyList<ProviderDatabaseOsStamp> _meaningfulOsStamps = [];
    private ChromelessButton? _nameButton;
    private string _osStampDetail = string.Empty;
    private string _osStampSummary = string.Empty;
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

    private string BadgeLabel => DatabaseStatusLocalizer.RowBadge(Localizer, Entry);

    private IReadOnlyDictionary<string, object>? CheckboxHiddenAttributes =>
        IsSelectionModeActive ? null : s_ariaHiddenTrueAttributes;

    private bool HasOsStamp => MeaningfulOsStamps.Count > 0;

    private bool IsRestoreBlocked => IsUpgradeBlocked || IsUpgrading || UpgradeProgress is not null;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private IReadOnlyList<ProviderDatabaseOsStamp> MeaningfulOsStamps => _meaningfulOsStamps;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private string OsStampAriaLabel => Localizer["Db_Entry_SourceOs", OsStampDetail];

    private string OsStampDetail => _osStampDetail;

    private string OsStampSummary => _osStampSummary;

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

    protected override void OnParametersSet()
    {
        _meaningfulOsStamps = Entry.OsStamps.Where(HasAnyField).ToList();
        _osStampDetail = string.Join("; ", _meaningfulOsStamps.Select(FormatStamp));
        _osStampSummary = _meaningfulOsStamps.Count switch
        {
            0 => string.Empty,
            1 => FormatStamp(_meaningfulOsStamps[0]),
            > OsStampDisplayCap => Localizer["Db_Entry_MixedOs_Capped"],
            _ => Localizer["Db_Entry_MixedOs_Count", _meaningfulOsStamps.Count]
        };
    }

    private static string? FormatBuildRevision(int? build, int? revision)
    {
        if (build is not > 0) { return null; }

        return revision is not null ?
            string.Create(CultureInfo.InvariantCulture, $"{build.Value}.{revision.Value}") :
            build.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool HasAnyField(ProviderDatabaseOsStamp stamp) =>
        stamp.Build is > 0 ||
        !string.IsNullOrEmpty(stamp.Edition) ||
        !string.IsNullOrEmpty(stamp.DisplayVersion);

    private string FormatStamp(ProviderDatabaseOsStamp stamp)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrEmpty(stamp.Edition)) { parts.Add(stamp.Edition); }

        if (!string.IsNullOrEmpty(stamp.DisplayVersion)) { parts.Add(stamp.DisplayVersion); }

        if (FormatBuildRevision(stamp.Build, stamp.Revision) is { } buildRevision) { parts.Add(buildRevision); }

        return parts.Count > 0 ? string.Join(" \u00B7 ", parts) : Localizer["Db_Entry_UnknownOs"];
    }

    private void HandleContextMenu(MouseEventArgs args)
    {
        if (IsSelectionModeActive) { return; }

        var items = new List<MenuItem>
        {
            MenuItem.Item(Localizer["Db_Entry_Remove"], () => OnRemove.InvokeAsync()),
        };

        MenuService.OpenAt(args.ClientX, args.ClientY, items, openedByKeyboard: ContextMenuInvocation.WasKeyboardTriggered(args));
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

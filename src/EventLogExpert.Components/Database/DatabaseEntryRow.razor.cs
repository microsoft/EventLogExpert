// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Database;

public sealed partial class DatabaseEntryRow : ComponentBase
{
    /// <summary>
    ///     Click-driven reveal flag for the trash strip on the left of the row. Set true when the name button is clicked,
    ///     cleared when the cursor leaves the row -- so re-entering the row without re-clicking does not re-open the slide,
    ///     even though the name button may still hold DOM focus. Keyboard navigation drives the reveal via :focus-visible in
    ///     CSS instead.
    /// </summary>
    private bool _isMouseRevealed;

    private enum ActionKind
    {
        None,
        Toggle,
        DisabledToggle,
        Upgrade,
        Retry,
        Spinner
    }

    [Parameter] public bool EffectiveEnabled { get; set; }

    [Parameter] public required DatabaseEntry Entry { get; set; }

    [Parameter] public bool IsClassificationPending { get; set; }

    [Parameter] public bool IsUpgradeBlocked { get; set; }

    [Parameter] public bool IsUpgrading { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnToggle { get; set; }

    [Parameter] public EventCallback OnUpgrade { get; set; }

    private string BadgeKind => Entry.BackupExists ? "Recovery" : Entry.Status.ToString();

    private string BadgeLabel => DatabaseStatusLabels.GetRowBadgeLabel(Entry);

    private ActionKind PrimaryAction
    {
        get
        {
            if (Entry.BackupExists) { return ActionKind.None; }

            if (IsUpgrading) { return ActionKind.Spinner; }

            return Entry.Status switch
            {
                DatabaseStatus.Ready =>
                    IsClassificationPending ? ActionKind.DisabledToggle : ActionKind.Toggle,
                DatabaseStatus.NotClassified => ActionKind.DisabledToggle,
                DatabaseStatus.UpgradeRequired => ActionKind.Upgrade,
                DatabaseStatus.UpgradeFailed => ActionKind.Retry,
                DatabaseStatus.UnrecognizedSchema => ActionKind.None,
                DatabaseStatus.ObsoleteSchema => ActionKind.None,
                DatabaseStatus.ClassificationFailed => ActionKind.None,
                _ => ActionKind.None
            };
        }
    }

    private bool ShouldShowBadge => Entry.BackupExists ||
        (!IsUpgrading &&
            Entry.Status != DatabaseStatus.Ready &&
            Entry.Status != DatabaseStatus.UpgradeRequired);

    private void HandleNameClick() => _isMouseRevealed = true;

    private void HandleRowMouseLeave() => _isMouseRevealed = false;
}

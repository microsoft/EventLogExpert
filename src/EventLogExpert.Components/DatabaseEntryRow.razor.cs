// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components;

public sealed partial class DatabaseEntryRow : ComponentBase
{
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

    private bool ShouldShowBadge =>
        Entry.BackupExists || (!IsUpgrading && Entry.Status != DatabaseStatus.Ready);
}

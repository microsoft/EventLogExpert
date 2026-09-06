// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.FilterLenses;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterLenses;

public sealed partial class LensBreadcrumb
{
    // "Save as group" stays keyboard-focusable while unavailable (aria-disabled instead of the native
    // disabled attribute, which drops focusability), so screen-reader users can reach it and hear the
    // reason via aria-describedby. The click/keyboard handler still guards the action (SaveAsGroupAsync).
    private readonly string _saveAsGroupHintId = $"lens-save-as-group-hint-{Guid.NewGuid():N}";

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    private bool CanSaveAsGroup => LensSource.Lenses.Any(lens => lens.Kind == LensKind.Property);

    [Inject] private IFilterLensCommands Commands { get; init; } = null!;

    [Inject] private IFilterLensSource LensSource { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private string SaveAsGroupAriaDisabled => CanSaveAsGroup ? "false" : "true";

    private string? SaveAsGroupHintRef => CanSaveAsGroup ? null : _saveAsGroupHintId;

    protected override void OnInitialized()
    {
        ObserveSource(LensSource);
        base.OnInitialized();
    }

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Escape") { return; }

        var lenses = LensSource.Lenses;

        if (!lenses.IsEmpty)
        {
            Commands.RemoveLens(lenses[^1].Id);
        }
    }

    private void SaveAll() => Commands.PromoteAllLenses();

    private async Task SaveAsGroupAsync()
    {
        if (!CanSaveAsGroup) { return; }

        PromptOutcome outcome = await AlertDialogService.DisplayPromptWithSecondary(
            Localizer["FilterLens_SaveAsGroup_PromptTitle"],
            Localizer["FilterLens_SaveAsGroup_PromptMessage"],
            Localizer["FilterLens_SaveAsGroup_DefaultName"],
            Localizer["FilterLens_SaveAsGroup_Save"],
            Localizer["FilterLens_SaveAsGroup_SaveAndClear"],
            Localizer["FilterLens_SaveAsGroup_Cancel"]);

        if (outcome.Choice == PromptChoice.Cancel || string.IsNullOrWhiteSpace(outcome.Value)) { return; }

        string name = outcome.Value.Trim();

        // Save and clear (Secondary) defers the clear to the persist-success effect, so a failed save leaves the active
        // lenses intact instead of discarding them.
        Commands.SaveLensesAsGroup(name, clearAfterSave: outcome.Choice == PromptChoice.Secondary);
    }
}

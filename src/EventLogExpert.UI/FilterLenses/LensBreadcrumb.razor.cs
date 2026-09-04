// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLenses;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterLenses;

public sealed partial class LensBreadcrumb
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private bool CanSaveAsGroup => LensSource.Lenses.Any(lens => lens.Kind == LensKind.Property);

    [Inject] private IFilterLensCommands Commands { get; init; } = null!;

    [Inject] private IFilterLensSource LensSource { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

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

    private void SaveAll()
    {
        Commands.PromoteAllLenses();
        AnnouncementService.Announce(Localizer["FilterLens_SavedAllAnnouncement"]);
    }

    private async Task SaveAsGroupAsync()
    {
        if (!CanSaveAsGroup) { return; }

        var name = await AlertDialogService.DisplayPrompt(
            Localizer["FilterLens_SaveAsGroup_PromptTitle"],
            Localizer["FilterLens_SaveAsGroup_PromptMessage"],
            Localizer["FilterLens_SaveAsGroup_DefaultName"]);

        if (string.IsNullOrWhiteSpace(name)) { return; }

        var trimmed = name.Trim();

        Commands.SaveLensesAsGroup(trimmed);
        AnnouncementService.Announce(Localizer["FilterLens_SavedAsGroupAnnouncement", trimmed]);
    }
}

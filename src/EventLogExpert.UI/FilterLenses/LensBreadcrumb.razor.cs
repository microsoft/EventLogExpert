// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.FilterLenses;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterLenses;

public sealed partial class LensBreadcrumb
{
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
}

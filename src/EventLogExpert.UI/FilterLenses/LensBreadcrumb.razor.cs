// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.FilterLenses;

public sealed partial class LensBreadcrumb
{
    [Inject] private IFilterLensCommands Commands { get; init; } = null!;

    [Inject] private IState<FilterLensState> LensState { get; init; } = null!;

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Escape") { return; }

        var lenses = LensState.Value.Lenses;

        // Escape scoped to the breadcrumb pops the most recently pushed lens, and stopPropagation keeps it from reaching
        // the event table's own Escape handler (which clears the selection). The breadcrumb only renders when a lens is
        // active, so a plain table Escape is unaffected.
        if (!lenses.IsEmpty)
        {
            Commands.RemoveLens(lenses[^1]);
        }
    }
}

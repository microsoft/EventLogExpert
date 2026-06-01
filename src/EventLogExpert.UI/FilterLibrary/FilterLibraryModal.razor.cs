// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.Modal;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class FilterLibraryModal : ModalBase<bool>
{
    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // Retry-on-reopen: re-dispatch when either the library hasn't loaded yet OR the prior
        // load failed (gives transient failures a chance to clear).
        if (!FilterLibraryState.Value.IsLoaded || FilterLibraryState.Value.LoadError)
        {
            FilterLibraryCommands.LoadLibrary();
        }
    }

    private Task HandleApplyAsync(string entryId)
    {
        FilterLibraryCommands.ApplyEntry(entryId);
        return CompleteAsync(true);
    }

    private Task HandleDeleteAsync(string entryId)
    {
        FilterLibraryCommands.DeleteEntry(entryId);
        return Task.CompletedTask;
    }
}

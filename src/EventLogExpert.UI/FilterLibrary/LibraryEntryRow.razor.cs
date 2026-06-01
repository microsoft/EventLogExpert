// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class LibraryEntryRow : ComponentBase
{
    [Parameter][EditorRequired]
    public required LibraryEntry Entry { get; set; }

    [Parameter]
    public EventCallback<LibraryEntryId> OnApply { get; set; }

    [Parameter]
    public EventCallback<LibraryEntryId> OnDelete { get; set; }

    [Parameter]
    public EventCallback<LibraryEntry> OnEdit { get; set; }

    private string KindLabel => Entry switch
    {
        LibraryEntrySavedFilter => "Filter",
        LibraryEntryPreset => "Preset",
        _ => "Unknown",
    };

    private Task HandleApplyClicked() => OnApply.InvokeAsync(Entry.Id);

    private Task HandleDeleteClicked() => OnDelete.InvokeAsync(Entry.Id);
}

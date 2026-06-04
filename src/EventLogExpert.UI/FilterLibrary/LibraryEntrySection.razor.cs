// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class LibraryEntrySection : ComponentBase
{
    private readonly string _panelId = $"library-section-{Guid.NewGuid():N}";

    private bool _isExpanded = true;

    [Parameter][EditorRequired] public ImmutableList<LibraryEntrySectionNode> ChildSections { get; set; } = [];

    [Parameter] public int Depth { get; set; }

    [Parameter][EditorRequired] public ImmutableList<LibraryEntry> DirectEntries { get; set; } = [];

    [Parameter][EditorRequired] public RenderFragment<LibraryEntry> EntryTemplate { get; set; } = null!;

    [Parameter][EditorRequired] public required string SectionName { get; set; }

    private int ChildSectionCount => ChildSections.Count;

    private int DirectEntryCount => DirectEntries.Count;

    private void Toggle() => _isExpanded = !_isExpanded;
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

/// <summary>
///     Reusable chrome for filter rows: handles the color picker, saved-state value display, action buttons, and
///     optional error banner. Subclasses supply <see cref="EditingContent" /> (and optionally
///     <see cref="ExtraEditingButtons" />) and wire the event callbacks back into <see cref="EditableFilterRowBase" />
///     hooks.
/// </summary>
public sealed partial class FilterRowChrome : ComponentBase
{
    [Parameter] public RenderFragment? EditingContent { get; set; }

    [Parameter] public string? ErrorMessage { get; set; }

    [Parameter] public RenderFragment? ExtraEditingButtons { get; set; }

    [Parameter] public FilterDraftModel? Filter { get; set; }

    [Parameter] public bool IsPending { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnSave { get; set; }

    [Parameter] public EventCallback OnToggleEnabled { get; set; }

    [Parameter] public EventCallback OnToggleExclusion { get; set; }

    [Parameter] public string? OuterCssClass { get; set; }

    [Parameter] public string? RightCssClass { get; set; }

    [Parameter] public bool ShowToggleEnabled { get; set; } = true;

    [Parameter] public bool UseInlineErrorRow { get; set; }

    [Parameter] public FilterModel? Value { get; set; }
}

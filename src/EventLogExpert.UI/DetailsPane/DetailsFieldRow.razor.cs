// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.DetailsPane;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.DetailsPane;

public sealed partial class DetailsFieldRow
{
    [Parameter]
    [EditorRequired]
    public DetailsField Field { get; set; } = null!;

    [Parameter]
    public bool IsExpanded { get; set; }

    [Parameter]
    public EventCallback OnCopy { get; set; }

    [Parameter]
    public EventCallback OnToggleExpand { get; set; }

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private static string PlaceholderKey(PlaceholderKind kind) =>
        kind switch
        {
            PlaceholderKind.Empty => "Details_Placeholder_Empty",
            PlaceholderKind.NoValues => "Details_Placeholder_NoValues",
            PlaceholderKind.NullValue => "Details_Placeholder_NullValue",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}

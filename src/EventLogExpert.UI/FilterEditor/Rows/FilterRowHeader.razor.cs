// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Rows;

public sealed partial class FilterRowHeader : ComponentBase
{
    /// <summary>
    ///     DOM id assigned to the comparison-text span; supplied by the shell so cross-component <c>aria-labelledby</c>
    ///     chains (e.g., the enable Toggle in <see cref="FilterRowActions" />) can reference it.
    /// </summary>
    [Parameter] public string? FilterLabelId { get; set; }

    /// <summary>Invoked when the user clicks the exclude/include toggle. New state is the bool argument.</summary>
    [Parameter] public EventCallback<bool> OnExclusionChanged { get; set; }

    [Parameter] public SavedFilter? Value { get; set; }
}

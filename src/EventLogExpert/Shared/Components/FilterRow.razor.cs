// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class FilterRow
{
    private bool _isEditing;

    [Parameter]
    public FilterModel Value { get; set; } = null!;

    private void EditFilter()
    {
        _isEditing = true;
    }

    private void RemoveFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value));
    }

    private void SaveFilter()
    {
        _isEditing = false;
    }
}

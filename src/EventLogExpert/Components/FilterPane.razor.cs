// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private readonly FilterDateModel _model = new();

    private bool _expandMenu;

    private string MenuState => _expandMenu.ToString().ToLower();

    private void AddFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddFilter());
        _expandMenu = true;
    }

    private void ApplyDateFilter() { }

    private void ResetDateFilter() { }

    private void ToggleMenu() => _expandMenu = !_expandMenu;
}

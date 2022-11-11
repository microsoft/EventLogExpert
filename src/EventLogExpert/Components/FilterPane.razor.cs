// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private readonly OldFilterModel _filter = new();

    private bool _expandMenu;

    private string MenuState
    {
        get
        {
            if (!FilterPaneState.Value.CurrentFilters.Any())
            {
                _expandMenu = false;
            }

            return _expandMenu.ToString().ToLower();
        }
    }

    private void AddFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddFilter());
        _expandMenu = true;
    }

    private void ToggleMenu() => _expandMenu = !_expandMenu;
}

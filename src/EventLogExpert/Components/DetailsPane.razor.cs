// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;

namespace EventLogExpert.Components;

public partial class DetailsPane
{
    private bool _expandMenu = false;

    private DisplayEventModel? Event { get; set; }

    protected override void Dispose(bool disposing)
    {
        ActionSubscriber.UnsubscribeFromAllActions(this);
        base.Dispose(disposing);
    }

    protected override void OnInitialized()
    {
        ActionSubscriber.SubscribeToAction<EventLogAction.SelectEvent>(this, UpdateDetails);
        base.OnInitialized();
    }

    private void ToggleMenu() => _expandMenu = !_expandMenu;

    private void UpdateDetails(EventLogAction.SelectEvent action)
    {
        Event = action.SelectedEvent;
        _expandMenu = true;
        StateHasChanged();
    }
}

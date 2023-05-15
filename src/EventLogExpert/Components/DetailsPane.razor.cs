// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;

namespace EventLogExpert.Components;

public partial class DetailsPane
{
    private bool _expandMenu = false;
    private bool _expandXml = false;

    private DisplayEventModel? Event { get; set; }

    protected override void OnInitialized()
    {
        SubscribeToAction<EventLogAction.SelectEvent>(UpdateDetails);
        base.OnInitialized();
    }

    private void CopyXml() => Clipboard.SetTextAsync(Event?.Xml);

    private void ToggleMenu() => _expandMenu = !_expandMenu;

    private void ToggleXml() => _expandXml = !_expandXml;

    private void UpdateDetails(EventLogAction.SelectEvent action)
    {
        Event = action.SelectedEvent;
        _expandMenu = true;
        _expandXml = false;
        StateHasChanged();
    }
}

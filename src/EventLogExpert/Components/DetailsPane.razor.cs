// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text;

namespace EventLogExpert.Components;

public sealed partial class DetailsPane
{
    private bool _hasOpened = false;
    private bool _isVisible = false;
    private bool _isXmlVisible = false;

    private DisplayEventModel? Event { get; set; }

    private string IsVisible => (EventLogState.Value.SelectedEvent is not null && _isVisible).ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventSelection { get; init; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("enableDetailsPaneResizer");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        SelectedEventSelection.Select(s => s.SelectedEvent);

        SelectedEventSelection.SelectedValueChanged += (s, v) =>
        {
            if (v is not null)
            {
                Event = EventLogState.Value.SelectedEvent;

                if (SettingsState.Value.Config.ShowDisplayPaneOnSelectionChange || !_hasOpened)
                {
                    _isVisible = true;
                }
            }
        };
    }

    private void CopyEvent()
    {
        StringBuilder stringToCopy = new();

        stringToCopy.AppendLine($"Log Name: {Event?.LogName}");
        stringToCopy.AppendLine($"Source: {Event?.Source}");
        stringToCopy.AppendLine($"Date: {Event?.TimeCreated.ConvertTimeZone(SettingsState.Value.Config.TimeZoneInfo)}");
        stringToCopy.AppendLine($"Event ID: {Event?.Id}");
        stringToCopy.AppendLine($"Task Category: {Event?.TaskCategory}");
        stringToCopy.AppendLine($"Level: {Event?.Level}");
        stringToCopy.AppendLine(Event?.KeywordsDisplayNames.GetEventKeywords());
        stringToCopy.AppendLine("User:"); // TODO: Update after DisplayEventModel is updated
        stringToCopy.AppendLine($"Computer: {Event?.ComputerName}");
        stringToCopy.AppendLine("Description:");
        stringToCopy.AppendLine(Event?.Description);
        stringToCopy.AppendLine("Event Xml:");
        stringToCopy.AppendLine(Event?.Xml);

        Clipboard.SetTextAsync(stringToCopy.ToString());
    }

    private void ToggleMenu()
    {
        if (!_hasOpened) { _hasOpened = true; }

        _isVisible = !_isVisible;
    }

    private void ToggleXml() => _isXmlVisible = !_isXmlVisible;
}

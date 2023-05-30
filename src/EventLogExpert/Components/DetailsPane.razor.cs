// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Text;

namespace EventLogExpert.Components;

public partial class DetailsPane
{
    [Inject] private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventSelection { get; set; } = null!;

    private bool _expandMenu = false;
    private bool _expandXml = false;

    private DisplayEventModel? Event { get; set; }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        SelectedEventSelection.Select(s => s.SelectedEvent);
        SelectedEventSelection.SelectedValueChanged += (s, v) =>
        {
            if (v != null)
            {
                Event = EventLogState.Value.SelectedEvent;
                _expandMenu = true;
                _expandXml = false;
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
        stringToCopy.AppendLine("Keywords:");
        stringToCopy.AppendLine("User:");  // TODO: Update after DisplayEventModel is updated
        stringToCopy.AppendLine($"Computer: {Event?.ComputerName}");
        stringToCopy.AppendLine("Description:");
        stringToCopy.AppendLine(Event?.Description);
        stringToCopy.AppendLine("Event Xml:");
        stringToCopy.AppendLine(Event?.Xml);

        Clipboard.SetTextAsync(stringToCopy.ToString());
    }

    private void ToggleMenu() => _expandMenu = !_expandMenu;

    private void ToggleXml() => _expandXml = !_expandXml;
}

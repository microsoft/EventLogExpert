// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Text;

namespace EventLogExpert.Components;

public partial class DetailsPane
{
    private bool _isVisible = false;
    private bool _isXmlVisible = false;

    private DisplayEventModel? Event { get; set; }

    [Inject] private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventSelection { get; set; } = null!;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        SelectedEventSelection.Select(s => s.SelectedEvent);

        SelectedEventSelection.SelectedValueChanged += (s, v) =>
        {
            if (v != null)
            {
                Event = EventLogState.Value.SelectedEvent;
                if (SettingsState.Value.Config.ShowDisplayPaneOnSelectionChange)
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
        stringToCopy.AppendLine(GetEventKeywords(Event?.KeywordsDisplayNames!));
        stringToCopy.AppendLine("User:"); // TODO: Update after DisplayEventModel is updated
        stringToCopy.AppendLine($"Computer: {Event?.ComputerName}");
        stringToCopy.AppendLine("Description:");
        stringToCopy.AppendLine(Event?.Description);
        stringToCopy.AppendLine("Event Xml:");
        stringToCopy.AppendLine(Event?.Xml);

        Clipboard.SetTextAsync(stringToCopy.ToString());
    }

    private string GetEventKeywords(IEnumerable<string> keywords)
    {
        StringBuilder sb = new("Keywords:");

        foreach (var keyword in keywords) { sb.Append($" {keyword}"); }

        return sb.ToString();
    }

    private void ToggleMenu() => _isVisible = !_isVisible;

    private void ToggleXml() => _isXmlVisible = !_isXmlVisible;
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using System.Text;
using System.Xml.Linq;

namespace EventLogExpert.Services;

public interface IClipboardService
{
    void CopySelectedEvent(CopyType? copyType = null);
}

public sealed class ClipboardService : IClipboardService
{
    private readonly IStateSelection<EventLogState, DisplayEventModel?> _selectedEvent;
    private readonly IState<SettingsState> _settingsState;

    public ClipboardService(
        IStateSelection<EventLogState, DisplayEventModel?> selectedEvent,
        IState<SettingsState> settingsState)
    {
        _selectedEvent = selectedEvent;
        _settingsState = settingsState;

        _selectedEvent.Select(s => s.SelectedEvent);
    }

    public void CopySelectedEvent(CopyType? copyType = null)
    {
        if (_selectedEvent.Value is null) { return; }

        StringBuilder stringToCopy = new();

        switch (copyType ?? _settingsState.Value.Config.CopyType)
        {
            case CopyType.Simple:
                stringToCopy.Append($"\"{_selectedEvent.Value.Level}\" ");
                stringToCopy.Append($"\"{_selectedEvent.Value.TimeCreated.ConvertTimeZone(_settingsState.Value.Config.TimeZoneInfo)}\" ");
                stringToCopy.Append($"\"{_selectedEvent.Value.Source}\" ");
                stringToCopy.Append($"\"{_selectedEvent.Value.Id}\" ");
                stringToCopy.Append($"\"{_selectedEvent.Value.Description}\"");

                Clipboard.SetTextAsync(stringToCopy.ToString());

                break;
            case CopyType.Xml:
                Clipboard.SetTextAsync(XElement.Parse(_selectedEvent.Value.Xml).ToString());

                break;
            case CopyType.Full:
            default:
                stringToCopy.AppendLine($"Log Name: {_selectedEvent.Value.LogName}");
                stringToCopy.AppendLine($"Source: {_selectedEvent.Value.Source}");
                stringToCopy.AppendLine($"Date: {_selectedEvent.Value.TimeCreated.ConvertTimeZone(_settingsState.Value.Config.TimeZoneInfo)}");
                stringToCopy.AppendLine($"Event ID: {_selectedEvent.Value.Id}");
                stringToCopy.AppendLine($"Task Category: {_selectedEvent.Value.TaskCategory}");
                stringToCopy.AppendLine($"Level: {_selectedEvent.Value.Level}");
                stringToCopy.AppendLine(_selectedEvent.Value.KeywordsDisplayNames.GetEventKeywords());
                stringToCopy.AppendLine("User:"); // TODO: Update after DisplayEventModel is updated
                stringToCopy.AppendLine($"Computer: {_selectedEvent.Value.ComputerName}");
                stringToCopy.AppendLine("Description:");
                stringToCopy.AppendLine(_selectedEvent.Value.Description);
                stringToCopy.AppendLine("Event Xml:");
                stringToCopy.AppendLine(XElement.Parse(_selectedEvent.Value.Xml).ToString());

                Clipboard.SetTextAsync(stringToCopy.ToString());

                break;
        }
    }
}

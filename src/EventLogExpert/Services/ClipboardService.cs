// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace EventLogExpert.Services;

public interface IClipboardService
{
    void CopySelectedEvent(CopyType? copyType = null);
}

public sealed class ClipboardService : IClipboardService
{
    private readonly IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> _selectedEvents;
    private readonly IState<SettingsState> _settingsState;

    public ClipboardService(
        IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> selectedEvents,
        IState<SettingsState> settingsState)
    {
        _selectedEvents = selectedEvents;
        _settingsState = settingsState;

        _selectedEvents.Select(s => s.SelectedEvents);
    }

    public void CopySelectedEvent(CopyType? copyType = null)
    {
        if (_selectedEvents.Value.IsEmpty) { return; }

        if (_selectedEvents.Value.Count == 1)
        {
            Clipboard.SetTextAsync(GetFormattedEvent(copyType, _selectedEvents.Value[0]));

            return;
        }

        StringBuilder stringToCopy = new();

        foreach (var selectedEvent in _selectedEvents.Value)
        {
            stringToCopy.AppendLine(GetFormattedEvent(copyType, selectedEvent));
        }

        Clipboard.SetTextAsync(stringToCopy.ToString());
    }

    private string GetFormattedEvent(CopyType? copyType, DisplayEventModel @event)
    {
        switch (copyType ?? _settingsState.Value.Config.CopyType)
        {
            case CopyType.Simple:
                StringBuilder simpleEvent = new();

                simpleEvent.Append($"\"{@event.Level}\" ");
                simpleEvent.Append($"\"{@event.TimeCreated.ConvertTimeZone(_settingsState.Value.Config.TimeZoneInfo)}\" ");
                simpleEvent.Append($"\"{@event.Source}\" ");
                simpleEvent.Append($"\"{@event.Id}\" ");
                simpleEvent.Append($"\"{@event.Description}\"");

                return simpleEvent.ToString();
            case CopyType.Xml:
                return XElement.Parse(@event.Xml).ToString();
            case CopyType.Full:
            default:
                StringBuilder fullEvent = new();

                fullEvent.AppendLine($"Log Name: {@event.LogName}");
                fullEvent.AppendLine($"Source: {@event.Source}");
                fullEvent.AppendLine($"Date: {@event.TimeCreated.ConvertTimeZone(_settingsState.Value.Config.TimeZoneInfo)}");
                fullEvent.AppendLine($"Event ID: {@event.Id}");
                fullEvent.AppendLine($"Task Category: {@event.TaskCategory}");
                fullEvent.AppendLine($"Level: {@event.Level}");
                fullEvent.AppendLine(@event.KeywordsDisplayNames.GetEventKeywords());
                fullEvent.AppendLine($"User: {@event.UserId}");
                fullEvent.AppendLine($"Computer: {@event.ComputerName}");
                fullEvent.AppendLine("Description:");
                fullEvent.AppendLine(@event.Description);
                fullEvent.AppendLine("Event Xml:");
                fullEvent.AppendLine(XElement.Parse(@event.Xml).ToString());

                return fullEvent.ToString();
        }
    }
}

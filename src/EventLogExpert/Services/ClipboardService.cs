// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
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
    private readonly IStateSelection<EventTableState, ImmutableDictionary<ColumnName, bool>> _eventTableColumns;
    private readonly IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> _selectedEvents;
    private readonly IState<SettingsState> _settingsState;

    public ClipboardService(
        IStateSelection<EventTableState, ImmutableDictionary<ColumnName, bool>> eventTableColumns,
        IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> selectedEvents,
        IState<SettingsState> settingsState)
    {
        _eventTableColumns = eventTableColumns;
        _selectedEvents = selectedEvents;
        _settingsState = settingsState;

        _eventTableColumns.Select(s => s.Columns);
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
            case CopyType.Default:
                StringBuilder defaultEvent = new();

                foreach ((ColumnName column, _) in _eventTableColumns.Value.Where(x => x.Value))
                {
                    switch (column)
                    {
                        case ColumnName.Level:
                            defaultEvent.Append($"\"{@event.Level}\" ");
                            break;
                        case ColumnName.DateAndTime:
                            defaultEvent.Append($"\"{@event.TimeCreated.ConvertTimeZone(_settingsState.Value.Config.TimeZoneInfo)}\" ");
                            break;
                        case ColumnName.ActivityId:
                            defaultEvent.Append($"\"{@event.ActivityId}\" ");
                            break;
                        case ColumnName.Log:
                            defaultEvent.Append($"\"{@event.OwningLog.Split("\\").Last()}\" ");
                            break;
                        case ColumnName.ComputerName:
                            defaultEvent.Append($"\"{@event.ComputerName}\" ");
                            break;
                        case ColumnName.Source:
                            defaultEvent.Append($"\"{@event.Source}\" ");
                            break;
                        case ColumnName.EventId:
                            defaultEvent.Append($"\"{@event.Id}\" ");
                            break;
                        case ColumnName.TaskCategory:
                            defaultEvent.Append($"\"{@event.TaskCategory}\" ");
                            break;
                        case ColumnName.Keywords:
                            defaultEvent.Append($"\"{string.Join(", ", @event.KeywordsDisplayNames)}\" ");
                            break;
                        case ColumnName.ProcessId:
                            defaultEvent.Append($"\"{@event.ProcessId}\" ");
                            break;
                        case ColumnName.ThreadId:
                            defaultEvent.Append($"\"{@event.ThreadId}\" ");
                            break;
                        case ColumnName.User:
                            defaultEvent.Append($"\"{@event.UserId}\" ");
                            break;
                    }
                }

                return defaultEvent.Append($"\"{@event.Description}\"").ToString();
            case CopyType.Simple:
                StringBuilder simpleEvent = new();

                simpleEvent.Append($"\"{@event.Level}\" ");
                simpleEvent.Append($"\"{@event.TimeCreated.ConvertTimeZone(_settingsState.Value.Config.TimeZoneInfo)}\" ");
                simpleEvent.Append($"\"{@event.Source}\" ");
                simpleEvent.Append($"\"{@event.Id}\" ");
                simpleEvent.Append($"\"{@event.Description}\"");

                return simpleEvent.ToString();
            case CopyType.Xml:
                return string.IsNullOrEmpty(@event.Xml) ?
                    string.Empty :
                    XElement.Parse(@event.Xml).ToString();
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

                if (!string.IsNullOrEmpty(@event.Xml))
                {
                    fullEvent.AppendLine(XElement.Parse(@event.Xml).ToString());
                }

                return fullEvent.ToString();
        }
    }
}

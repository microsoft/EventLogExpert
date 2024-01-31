// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using System.Text;

namespace EventLogExpert.Services;

public interface IClipboardService
{
    void CopySelectedEvent(CopyType? copyType = null);
}

public sealed class ClipboardService(IState<EventLogState> eventLogState, IState<SettingsState> settingsState)
    : IClipboardService
{
    public void CopySelectedEvent(CopyType? copyType = null)
    {
        if (eventLogState.Value.SelectedEvent is null) { return; }

        StringBuilder stringToCopy = new();

        switch (copyType ?? settingsState.Value.Config.CopyType)
        {
            case CopyType.Simple :
                stringToCopy.Append($"\"{eventLogState.Value.SelectedEvent.Level}\" ");

                stringToCopy.Append(
                    $"\"{eventLogState.Value.SelectedEvent.TimeCreated.ConvertTimeZone(settingsState.Value.Config.TimeZoneInfo)}\" ");

                stringToCopy.Append($"\"{eventLogState.Value.SelectedEvent.Source}\" ");
                stringToCopy.Append($"\"{eventLogState.Value.SelectedEvent.Id}\" ");
                stringToCopy.Append($"\"{eventLogState.Value.SelectedEvent.Description}\"");

                Clipboard.SetTextAsync(stringToCopy.ToString());
                break;
            case CopyType.Xml :
                Clipboard.SetTextAsync(eventLogState.Value.SelectedEvent.Xml);
                break;
            case CopyType.Full :
            default :
                stringToCopy.AppendLine($"Log Name: {eventLogState.Value.SelectedEvent.LogName}");
                stringToCopy.AppendLine($"Source: {eventLogState.Value.SelectedEvent.Source}");

                stringToCopy.AppendLine(
                    $"Date: {eventLogState.Value.SelectedEvent.TimeCreated.ConvertTimeZone(settingsState.Value.Config.TimeZoneInfo)}");

                stringToCopy.AppendLine($"Event ID: {eventLogState.Value.SelectedEvent.Id}");
                stringToCopy.AppendLine($"Task Category: {eventLogState.Value.SelectedEvent.TaskCategory}");
                stringToCopy.AppendLine($"Level: {eventLogState.Value.SelectedEvent.Level}");
                stringToCopy.AppendLine(eventLogState.Value.SelectedEvent.KeywordsDisplayNames.GetEventKeywords());
                stringToCopy.AppendLine("User:"); // TODO: Update after DisplayEventModel is updated
                stringToCopy.AppendLine($"Computer: {eventLogState.Value.SelectedEvent.ComputerName}");
                stringToCopy.AppendLine("Description:");
                stringToCopy.AppendLine(eventLogState.Value.SelectedEvent.Description);
                stringToCopy.AppendLine("Event Xml:");
                stringToCopy.AppendLine(eventLogState.Value.SelectedEvent.Xml);

                Clipboard.SetTextAsync(stringToCopy.ToString());
                break;
        }
    }
}

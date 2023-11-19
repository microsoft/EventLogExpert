// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using System.Text;

namespace EventLogExpert.Services;

public interface IClipboardService
{
    void CopySelectedEvent(DisplayEventModel? @event, CopyType? copyType);
}

public sealed class ClipboardService(IState<SettingsState> settingsState) : IClipboardService
{
    public void CopySelectedEvent(DisplayEventModel? @event, CopyType? copyType)
    {
        if (@event is null) { return; }

        StringBuilder stringToCopy = new();

        switch (copyType)
        {
            case CopyType.Simple :
                stringToCopy.Append($"\"{@event.Level}\" ");
                stringToCopy.Append($"\"{@event.TimeCreated.ConvertTimeZone(settingsState.Value.Config.TimeZoneInfo)}\" ");
                stringToCopy.Append($"\"{@event.Source}\" ");
                stringToCopy.Append($"\"{@event.Id}\" ");
                stringToCopy.Append($"\"{@event.Description}\"");

                Clipboard.SetTextAsync(stringToCopy.ToString());
                break;
            case CopyType.Xml :
                Clipboard.SetTextAsync(@event.Xml);
                break;
            case CopyType.Full :
            default :
                stringToCopy.AppendLine($"Log Name: {@event.LogName}");
                stringToCopy.AppendLine($"Source: {@event.Source}");
                stringToCopy.AppendLine($"Date: {@event.TimeCreated.ConvertTimeZone(settingsState.Value.Config.TimeZoneInfo)}");
                stringToCopy.AppendLine($"Event ID: {@event.Id}");
                stringToCopy.AppendLine($"Task Category: {@event.TaskCategory}");
                stringToCopy.AppendLine($"Level: {@event.Level}");
                stringToCopy.AppendLine(@event.KeywordsDisplayNames.GetEventKeywords());
                stringToCopy.AppendLine("User:"); // TODO: Update after DisplayEventModel is updated
                stringToCopy.AppendLine($"Computer: {@event.ComputerName}");
                stringToCopy.AppendLine("Description:");
                stringToCopy.AppendLine(@event.Description);
                stringToCopy.AppendLine("Event Xml:");
                stringToCopy.AppendLine(@event.Xml);

                Clipboard.SetTextAsync(stringToCopy.ToString());
                break;
        }
    }
}

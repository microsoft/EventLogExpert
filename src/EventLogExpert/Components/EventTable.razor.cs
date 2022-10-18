namespace EventLogExpert.Components;

public partial class EventTable
{
    private readonly Dictionary<string, int> _colWidths = new()
    {
        { "RecordId", 10 },
        { "TimeCreated", 25 },
        { "Id", 10 },
        { "MachineName", 10 },
        { "Level", 15 },
        { "ProviderName", 25 },
        { "Task", 20 },
        { "Description", 200 }
    };

    //private void OnFilterInput(string eventId)
    //{
    //    if (int.TryParse(eventId, out int id) is not true)
    //    {
    //        _displayEvents = EventLogState.Value.EventsToDisplay.ToList();
    //        return;
    //    }

    //    _displayEvents = EventLogState.Value.EventsToDisplay.Where(entry => entry.Id == id).ToList();
    //}
}

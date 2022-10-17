using EventLogExpert.Library.Models;
using EventLogExpert.Store.Actions;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private string filterEventId = "";
    private string filterDescription = "";

    private void ApplyFilter()
    {
        var comparisonsToPerform = new List<Func<DisplayEventModel, bool>>();
        var filterStrings = new List<string>();

        if (filterEventId.Length > 0)
        {
            var eventId = int.Parse(filterEventId);
            comparisonsToPerform.Add(e => e.Id == eventId);
            filterStrings.Add($"EventId == {eventId}");
        }

        if (filterDescription.Length > 0)
        {
            comparisonsToPerform.Add(e =>
                e.Description.Contains(filterDescription, StringComparison.OrdinalIgnoreCase));

            filterStrings.Add($"Description contains '{filterDescription}'");
        }

        var filterText = string.Join(" && ", filterStrings);
        Dispatcher.Dispatch(new FilterPaneAction.AddRecentFilter(filterText));
        Dispatcher.Dispatch(new EventLogAction.FilterEvents(comparisonsToPerform));
    }
}
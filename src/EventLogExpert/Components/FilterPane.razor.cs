using EventLogExpert.Library.Models;
using EventLogExpert.Store.Actions;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private readonly FilterModel _filter = new();

    private void ApplyFilter()
    {
        var filterStrings = new List<string>();
        List<Func<DisplayEventModel, bool>> comparisonsToPerform = new();

        if (_filter.Id != -1)
        {
            comparisonsToPerform.Add(e => e.Id == _filter.Id);
            filterStrings.Add($"EventId == {_filter.Id}");
        }

        if (!string.IsNullOrWhiteSpace(_filter.Description))
        {
            comparisonsToPerform.Add(e =>
                e.Description.Contains(_filter.Description, StringComparison.OrdinalIgnoreCase));

            filterStrings.Add($"Description contains '{_filter.Description}'");
        }

        var filterText = string.Join(" && ", filterStrings);
        Dispatcher.Dispatch(new FilterPaneAction.AddRecentFilter(filterText));
        Dispatcher.Dispatch(new EventLogAction.FilterEvents(comparisonsToPerform));
    }

    private void ResetFilter()
    {
        _filter.Id = -1;
        _filter.Description = string.Empty;
        Dispatcher.Dispatch(new EventLogAction.ClearFilters());
    }
}

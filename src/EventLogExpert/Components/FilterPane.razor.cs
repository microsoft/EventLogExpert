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

        //if (filterDescription.Length > 0)
        //{
        //    comparisonsToPerform.Add(e =>
        //        e.Description.Contains(filterDescription, StringComparison.OrdinalIgnoreCase));

        //    filterStrings.Add($"Description contains '{filterDescription}'");
        //}

        var filterText = string.Join(" && ", filterStrings);
        Dispatcher.Dispatch(new FilterPaneAction.AddRecentFilter(filterText));
        Dispatcher.Dispatch(new EventLogAction.FilterEvents(comparisonsToPerform));
    }

    private void ResetFilter()
    {
        _filter.Id = -1;
        Dispatcher.Dispatch(new EventLogAction.ClearFilters());
    }
}

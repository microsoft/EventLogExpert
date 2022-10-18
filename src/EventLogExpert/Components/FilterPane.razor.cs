using EventLogExpert.Library.Models;
using EventLogExpert.Store.Actions;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private readonly List<Func<DisplayEventModel, bool>> _comparisonsToPerform = new();
    private readonly FilterModel _filter = new();

    private void ApplyFilter()
    {
        var filterStrings = new List<string>();
        _comparisonsToPerform.Clear();

        if (_filter.Id != -1)
        {
            _comparisonsToPerform.Add(e => e.Id == _filter.Id);
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
        Dispatcher.Dispatch(new EventLogAction.FilterEvents(_comparisonsToPerform));
    }

    private void ResetFilter()
    {
        _comparisonsToPerform.Clear();
        _filter.Id = -1;
        _comparisonsToPerform.Add(e => e.Id == _filter.Id);
        Dispatcher.Dispatch(new EventLogAction.FilterEvents(_comparisonsToPerform));
    }
}

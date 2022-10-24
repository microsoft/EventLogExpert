// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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

        // TODO: Break these into separate functions for future multi select filtering
        if (_filter.Id != -1)
        {
            comparisonsToPerform.Add(e => e.Id == _filter.Id);
            filterStrings.Add($"EventId == {_filter.Id}");
        }

        if (_filter.Level is not null)
        {
            comparisonsToPerform.Add(e => e.Level == _filter.Level);
            filterStrings.Add($"Severity == {_filter.Level}");
        }

        if (!string.IsNullOrWhiteSpace(_filter.Provider))
        {
            comparisonsToPerform.Add(e => string.Equals(e.ProviderName, _filter.Provider));
            filterStrings.Add($"ProviderName == '{_filter.Provider}'");
        }

        if (!string.IsNullOrWhiteSpace(_filter.Task))
        {
            comparisonsToPerform.Add(e => string.Equals(e.TaskDisplayName, _filter.Task));
            filterStrings.Add($"Description contains '{_filter.Task}'");
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
        _filter.Level = null;
        _filter.Provider = string.Empty;
        _filter.Task = string.Empty;
        _filter.Description = string.Empty;
        Dispatcher.Dispatch(new EventLogAction.ClearFilters());
    }
}

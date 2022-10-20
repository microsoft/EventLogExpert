using EventLogExpert.Library.Models;
using EventLogExpert.Store.State;

namespace EventLogExpert.Store.Actions;

public record EventLogAction
{
    public record OpenLog(EventLogState.LogSpecifier LogSpecifier) : EventLogAction;

    public record ClearEvents : EventLogAction;

    public record LoadEvents(
        List<DisplayEventModel> Events,
        IReadOnlyList<int> AllEventIds,
        IReadOnlyList<string> AllProviderNames,
        IReadOnlyList<string> AllTaskNames
    ) : EventLogAction;

    public record ClearFilters : EventLogAction;

    public record FilterEvents(List<Func<DisplayEventModel, bool>> Filter) : EventLogAction;
}

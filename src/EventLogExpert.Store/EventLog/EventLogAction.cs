// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Store.EventLog;

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

    public record FilterEvents(IEnumerable<Func<DisplayEventModel, bool>> Filters) : EventLogAction;
}

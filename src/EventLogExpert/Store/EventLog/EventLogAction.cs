// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Store.EventLog;

public record EventLogAction
{
    public record AddEvent(DisplayEventModel NewEvent) : EventLogAction;

    public record LoadEvents(
        string LogName,
        List<DisplayEventModel> Events,
        IReadOnlyList<int> AllEventIds,
        IReadOnlyList<string> AllProviderNames,
        IReadOnlyList<string> AllTaskNames
    ) : EventLogAction;

    public record LoadNewEvents() : EventLogAction;

    public record OpenLog(EventLogState.LogSpecifier LogSpecifier) : EventLogAction;

    public record CloseLog(string LogName) : EventLogAction;

    public record CloseAll() : EventLogAction;

    public record SelectEvent(DisplayEventModel? SelectedEvent) : EventLogAction;

    public record SetContinouslyUpdate(bool ContinuouslyUpdate) : EventLogAction;

    public record SetEventsLoading(int Count) : EventLogAction;
}

﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Store.EventLog;

public record EventLogAction
{
    public record AddEvent(DisplayEventModel NewEvent) : EventLogAction;

    public record ClearEvents : EventLogAction;

    public record LoadEvents(
        List<DisplayEventModel> Events,
        LiveLogWatcher Watcher,
        IReadOnlyList<int> AllEventIds,
        IReadOnlyList<string> AllProviderNames,
        IReadOnlyList<string> AllTaskNames
    ) : EventLogAction;

    public record LoadNewEvents() : EventLogAction;

    public record OpenLog(EventLogState.LogSpecifier LogSpecifier) : EventLogAction;

    public record SelectEvent(DisplayEventModel? SelectedEvent) : EventLogAction;

    public record SetContinouslyUpdate(bool ContinuouslyUpdate) : EventLogAction;
}

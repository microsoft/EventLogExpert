﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record EventLogAction
{
    public sealed record AddEvent(DisplayEventModel NewEvent);

    public sealed record AddEventBuffered(ReadOnlyCollection<DisplayEventModel> UpdatedBuffer, bool IsFull);

    public sealed record AddEventSuccess(ImmutableDictionary<string, EventLogData> ActiveLogs);

    public sealed record CloseAll;

    public sealed record CloseLog(string LogName);

    public sealed record LoadEvents(
        string LogName,
        LogType Type,
        List<DisplayEventModel> Events,
        IEnumerable<int> AllEventIds,
        IEnumerable<Guid?> AllActivityIds,
        IEnumerable<string> AllProviderNames,
        IEnumerable<string> AllTaskNames,
        IEnumerable<string> AllKeywords);

    public sealed record LoadNewEvents;

    public sealed record OpenLog(string LogName, LogType LogType, CancellationToken Token = default);

    public sealed record SelectEvent(DisplayEventModel? SelectedEvent);

    public sealed record SetContinouslyUpdate(bool ContinuouslyUpdate);

    public sealed record SetFilters(EventFilter EventFilter);
}

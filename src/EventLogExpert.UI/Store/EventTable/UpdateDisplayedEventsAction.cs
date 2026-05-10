// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.EventTable;

public sealed record UpdateDisplayedEventsAction(
    IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> ActiveLogs);

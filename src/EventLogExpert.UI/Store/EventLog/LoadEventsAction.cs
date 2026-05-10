// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record LoadEventsAction(EventLogData LogData, IReadOnlyList<ResolvedEvent> Events);

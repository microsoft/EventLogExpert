// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.EventLog;

public sealed record LoadEventsAction(EventLogData LogData, IReadOnlyList<ResolvedEvent> Events);

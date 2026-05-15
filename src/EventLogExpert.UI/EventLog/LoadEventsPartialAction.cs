// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.UI.EventLog;

public sealed record LoadEventsPartialAction(EventLogData LogData, IReadOnlyList<ResolvedEvent> Events);

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.EventLog;

internal sealed record LoadEventsPartialAction(EventLogData LogData, IReadOnlyList<ResolvedEvent> Events);

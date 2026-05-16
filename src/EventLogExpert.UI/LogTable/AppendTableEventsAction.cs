// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.LogTable;

public sealed record AppendTableEventsAction(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events);

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

public sealed record UpdateTableAction(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events);

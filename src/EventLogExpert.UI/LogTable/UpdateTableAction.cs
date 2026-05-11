// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.EventLog;

namespace EventLogExpert.UI.LogTable;

public sealed record UpdateTableAction(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events);

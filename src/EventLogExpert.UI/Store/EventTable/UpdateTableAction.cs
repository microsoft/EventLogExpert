// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.EventTable;

public sealed record UpdateTableAction(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events);

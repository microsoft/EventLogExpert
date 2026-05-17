// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.EventLog;

internal sealed record SelectEventsAction(IReadOnlyCollection<ResolvedEvent> SelectedEvents);

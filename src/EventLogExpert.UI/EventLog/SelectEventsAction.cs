// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.EventLog;

public sealed record SelectEventsAction(IReadOnlyCollection<ResolvedEvent> SelectedEvents);

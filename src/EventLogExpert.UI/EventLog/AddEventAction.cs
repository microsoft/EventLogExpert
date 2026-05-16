// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.EventLog;

internal sealed record AddEventAction(ResolvedEvent NewEvent);

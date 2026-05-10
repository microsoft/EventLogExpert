// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record AddEventAction(ResolvedEvent NewEvent);

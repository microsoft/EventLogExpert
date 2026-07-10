// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.EventLog;

internal sealed record SelectEventsAction(IReadOnlyCollection<SelectionEntry> Selection);

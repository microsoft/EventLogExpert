// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.EventLog;

internal sealed record EventBufferedAction(IReadOnlyList<ResolvedEvent> UpdatedBuffer, bool IsFull);

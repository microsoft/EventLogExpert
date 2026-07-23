// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.EventLog;

// Removes only the buffer entries captured for a completed flush, by reference identity, so an event a watcher buffers
// mid-flush survives (a blanket clear would drop it).
internal sealed record NewEventBufferConsumedAction(IReadOnlyList<ResolvedEvent> ConsumedEvents);

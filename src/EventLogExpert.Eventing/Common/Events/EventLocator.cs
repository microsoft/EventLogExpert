// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Eventing.Common.Events;

public readonly record struct EventLocator(EventLogId LogId, int Generation, int Index);

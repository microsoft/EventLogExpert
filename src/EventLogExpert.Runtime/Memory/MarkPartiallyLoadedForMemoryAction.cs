// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.Memory;

internal sealed record MarkPartiallyLoadedForMemoryAction(EventLogId LogId);

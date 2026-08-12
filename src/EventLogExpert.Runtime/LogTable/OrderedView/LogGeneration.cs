// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal readonly record struct LogGeneration(EventLogId LogId, int Generation);

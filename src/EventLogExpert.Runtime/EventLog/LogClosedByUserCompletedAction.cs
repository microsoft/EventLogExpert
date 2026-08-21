// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.EventLog;

internal sealed record LogClosedByUserCompletedAction(EventLogId LogId);

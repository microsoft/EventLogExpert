// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.EventLog;

internal sealed record LogClosedByUserAction(EventLogId LogId, string LogName);

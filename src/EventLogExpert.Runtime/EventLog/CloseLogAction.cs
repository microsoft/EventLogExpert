// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.EventLog;

internal sealed record CloseLogAction(EventLogId LogId, string LogName);

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.UI.EventLog;

internal sealed record CloseLogAction(EventLogId LogId, string LogName);

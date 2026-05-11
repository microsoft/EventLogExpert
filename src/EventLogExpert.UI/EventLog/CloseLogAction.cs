// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.EventLog;

public sealed record CloseLogAction(EventLogId LogId, string LogName);

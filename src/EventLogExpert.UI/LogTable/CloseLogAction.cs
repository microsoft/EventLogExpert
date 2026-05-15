// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.UI.LogTable;

public sealed record CloseLogAction(EventLogId LogId);

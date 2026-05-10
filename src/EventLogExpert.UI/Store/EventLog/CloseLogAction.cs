// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record CloseLogAction(EventLogId LogId, string LogName);

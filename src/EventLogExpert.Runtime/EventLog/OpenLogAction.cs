// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.EventLog;

internal sealed record OpenLogAction(string LogName, LogPathType LogPathType, CancellationToken Token = default, EventLogId? PreassignedId = null);

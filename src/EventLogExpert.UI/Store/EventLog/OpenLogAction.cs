// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record OpenLogAction(string LogName, LogPathType LogPathType, CancellationToken Token = default);

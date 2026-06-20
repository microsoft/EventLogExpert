// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable;

internal sealed record RemoveTabFromGroupAction(EventLogId TabId);

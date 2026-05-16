// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Immutable;

namespace EventLogExpert.UI.EventLog;

internal sealed record AddEventSuccessAction(ImmutableDictionary<string, EventLogData> ActiveLogs);

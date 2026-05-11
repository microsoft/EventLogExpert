// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.EventLog;

public sealed record AddEventSuccessAction(ImmutableDictionary<string, EventLogData> ActiveLogs);

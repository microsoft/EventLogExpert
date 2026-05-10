// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record AddEventSuccessAction(ImmutableDictionary<string, EventLogData> ActiveLogs);

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed record FilteredPresenceInvalidatedAction(long FilterVersion, ImmutableArray<EventLogId> LogIds);

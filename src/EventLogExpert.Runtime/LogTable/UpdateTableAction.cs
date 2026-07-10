// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable;

public sealed record UpdateTableAction(EventLogId LogId)
{
    internal EventColumnView? View { get; init; }

    internal int Version { get; init; }
}

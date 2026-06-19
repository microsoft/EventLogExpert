// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable;

public sealed record LogView(EventLogId Id)
{
    public string? FileName { get; init; }

    public string ComputerName { get; init; } = string.Empty;

    public string LogName { get; init; } = string.Empty;

    public LogPathType LogPathType { get; init; }

    // null = standalone single-log tab; a group id (incl. the implicit AllLogs) = a merged tab.
    public LogTabGroupId? GroupId { get; init; }

    public bool IsCombined => GroupId is not null;

    public bool IsLoading { get; init; }
}

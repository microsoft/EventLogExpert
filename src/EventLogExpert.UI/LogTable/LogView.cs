// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.LogTable;

public sealed record LogView(EventLogId Id)
{
    public string? FileName { get; init; }

    public string ComputerName { get; init; } = string.Empty;

    public string LogName { get; init; } = string.Empty;

    public LogPathType LogPathType { get; init; }

    public bool IsCombined { get; init; }

    public bool IsLoading { get; init; }
}

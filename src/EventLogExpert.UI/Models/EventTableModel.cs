// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;

namespace EventLogExpert.UI.Models;

public sealed record EventTableModel(EventLogId Id)
{
    public string? FileName { get; init; }

    public string ComputerName { get; init; } = string.Empty;

    public string LogName { get; init; } = string.Empty;

    public PathType PathType { get; init; }

    public bool IsCombined { get; init; }

    public bool IsLoading { get; init; }
}

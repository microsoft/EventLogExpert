// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;

namespace EventLogExpert.UI.Models;

public sealed record EventTableModel(EventLogId Id)
{
    public string? FileName { get; init; }

    public string ComputerName => DisplayedEvents.FirstOrDefault()?.ComputerName ?? string.Empty;

    public string LogName { get; init; } = string.Empty;

    public PathType PathType { get; init; }

    public IReadOnlyList<DisplayEventModel> DisplayedEvents { get; init; } = [];

    public bool IsCombined { get; init; }

    public bool IsLoading { get; init; }
}

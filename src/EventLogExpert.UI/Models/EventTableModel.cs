// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using Microsoft.AspNetCore.Components;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Models;

public sealed record EventTableModel
{
    public Guid Id { get; } = Guid.NewGuid();

    public string? FileName { get; init; }

    public string ComputerName => DisplayedEvents.FirstOrDefault()?.ComputerName ?? string.Empty;

    public string LogName { get; init; } = string.Empty;

    public LogType LogType { get; init; }

    // Yes this is mutable but HTML refs are only directly assigned to fields and properties
    public ElementReference? ElementReference { get; set; }

    public ReadOnlyCollection<DisplayEventModel> DisplayedEvents { get; init; } =
        new List<DisplayEventModel>().AsReadOnly();

    public bool IsCombined { get; init; }

    public bool IsLoading { get; init; }
}

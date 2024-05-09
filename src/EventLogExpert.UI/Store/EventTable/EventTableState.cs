// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.EventTable;

[FeatureState]
public sealed record EventTableState
{
    public ImmutableList<EventTableModel> EventTables { get; init; } = [];

    public EventLogId? ActiveEventLogId { get; init; }

    public ImmutableDictionary<ColumnName, bool> Columns { get; init; } = ImmutableDictionary<ColumnName, bool>.Empty;

    public ColumnName? OrderBy { get; init; }

    public bool IsDescending { get; init; } = true;
}

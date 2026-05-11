// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.EventLog;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.LogTable;

[FeatureState]
public sealed record LogTableState
{
    public ImmutableList<LogView> EventTables { get; init; } = [];

    public IReadOnlyList<ResolvedEvent> DisplayedEvents { get; init; } = [];

    public ImmutableDictionary<EventLogId, int> EventCountByLog { get; init; } =
        ImmutableDictionary<EventLogId, int>.Empty;

    public EventLogId? ActiveEventLogId { get; init; }

    public ImmutableDictionary<ColumnName, bool> Columns { get; init; } = ImmutableDictionary<ColumnName, bool>.Empty;

    public ImmutableDictionary<ColumnName, int> ColumnWidths { get; init; } = ImmutableDictionary<ColumnName, int>.Empty;

    public ImmutableList<ColumnName> ColumnOrder { get; init; } = [];

    public ColumnName? OrderBy { get; init; }

    public bool IsDescending { get; init; } = true;
}

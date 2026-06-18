// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class LogTableStateExportTests
{
    [Fact]
    public void GetActiveDisplayedEvents_NoActiveTable_ReturnsEmpty()
    {
        var state = new LogTableState();

        Assert.Empty(state.GetActiveDisplayedEvents());
    }

    [Fact]
    public void GetOrderedEnabledColumns_AppendsEnabledColumnsMissingFromOrder()
    {
        var state = new LogTableState
        {
            Columns = ImmutableDictionary<ColumnName, bool>.Empty
                .Add(ColumnName.Source, true)
                .Add(ColumnName.EventId, true),
            ColumnOrder = [ColumnName.Source]
        };

        var ordered = state.GetOrderedEnabledColumns(new ColumnDefaults());

        Assert.Equal(ColumnName.Source, ordered[0]);
        Assert.Contains(ColumnName.EventId, ordered);
        Assert.Equal(2, ordered.Count);
    }

    [Fact]
    public void GetOrderedEnabledColumns_DuplicateOrderAndMissingEnabledColumn_DeduplicatesThenAppends()
    {
        var state = new LogTableState
        {
            Columns = ImmutableDictionary<ColumnName, bool>.Empty
                .Add(ColumnName.Source, true)
                .Add(ColumnName.EventId, true),
            ColumnOrder = [ColumnName.Source, ColumnName.Source]
        };

        var ordered = state.GetOrderedEnabledColumns(new ColumnDefaults());

        // Source deduplicated to one entry; EventId (enabled, absent from the order) appended from defaults.
        Assert.Equal(ColumnName.Source, ordered[0]);
        Assert.Contains(ColumnName.EventId, ordered);
        Assert.Equal(2, ordered.Count);
    }

    [Fact]
    public void GetOrderedEnabledColumns_FiltersToEnabled_InColumnOrder()
    {
        var state = new LogTableState
        {
            Columns = ImmutableDictionary<ColumnName, bool>.Empty
                .Add(ColumnName.Source, true)
                .Add(ColumnName.EventId, true)
                .Add(ColumnName.Level, false),
            ColumnOrder = [ColumnName.EventId, ColumnName.Source, ColumnName.Level]
        };

        var ordered = state.GetOrderedEnabledColumns(new ColumnDefaults());

        ColumnName[] expected = [ColumnName.EventId, ColumnName.Source];
        Assert.Equal(expected, ordered);
    }

    [Fact]
    public void GetOrderedEnabledColumns_WithDuplicatesInColumnOrder_DeduplicatesPreservingFirstOccurrence()
    {
        var state = new LogTableState
        {
            Columns = ImmutableDictionary<ColumnName, bool>.Empty
                .Add(ColumnName.Source, true)
                .Add(ColumnName.EventId, true),
            ColumnOrder = [ColumnName.Source, ColumnName.EventId, ColumnName.Source]
        };

        var ordered = state.GetOrderedEnabledColumns(new ColumnDefaults());

        ColumnName[] expected = [ColumnName.Source, ColumnName.EventId];
        Assert.Equal(expected, ordered);
    }
}

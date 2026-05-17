// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using Fluxor;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class LogTableCommands(IDispatcher dispatcher) : ILogTableCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void LoadColumns() => _dispatcher.Dispatch(new LoadColumnsAction());

    public void ReorderColumn(ColumnName column, ColumnName target, bool insertAfter) =>
        _dispatcher.Dispatch(new ReorderColumnAction(column, target, insertAfter));

    public void ResetColumnDefaults() => _dispatcher.Dispatch(new ResetColumnDefaultsAction());

    public void SetActiveTable(EventLogId logId) => _dispatcher.Dispatch(new SetActiveTableAction(logId));

    public void SetColumnWidth(ColumnName column, int width) => _dispatcher.Dispatch(new SetColumnWidthAction(column, width));

    public void SetOrderBy(ColumnName? orderBy) => _dispatcher.Dispatch(new SetOrderByAction(orderBy));

    public void ToggleColumn(ColumnName column) => _dispatcher.Dispatch(new ToggleColumnAction(column));

    public void ToggleSortDirection() => _dispatcher.Dispatch(new ToggleSortingAction());
}

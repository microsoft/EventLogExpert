// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.LogTable;

public sealed class Effects(ILogTablePreferencesProvider preferencesProvider, IState<LogTableState> logTableState)
{
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly ILogTablePreferencesProvider _preferencesProvider = preferencesProvider;

    [EffectMethod(typeof(LoadColumnsAction))]
    public Task HandleLoadColumns(IDispatcher dispatcher)
    {
        var columns = new Dictionary<ColumnName, bool>();
        var enabledColumns = _preferencesProvider.EnabledEventTableColumnsPreference;

        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            columns.Add(column, enabledColumns.Contains(column));
        }

        var widths = BuildWidths();
        var order = BuildOrder();

        dispatcher.Dispatch(new LoadColumnsCompletedAction(columns, widths, order));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleReorderColumn(ReorderColumnAction action, IDispatcher dispatcher)
    {
        // Read from post-reducer state to avoid race conditions with rapid reorder actions
        _preferencesProvider.ColumnOrderPreference = _logTableState.Value.ColumnOrder;

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ResetColumnDefaultsAction))]
    public Task HandleResetColumnDefaults(IDispatcher dispatcher)
    {
        var columns = new Dictionary<ColumnName, bool>();

        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            columns.Add(column, ColumnDefaults.EnabledColumns.Contains(column));
        }

        _preferencesProvider.EnabledEventTableColumnsPreference = ColumnDefaults.EnabledColumns;
        _preferencesProvider.ColumnWidthsPreference = new Dictionary<ColumnName, int>();
        _preferencesProvider.ColumnOrderPreference = [];

        var widths = new Dictionary<ColumnName, int>(ColumnDefaults.Widths);

        dispatcher.Dispatch(new LoadColumnsCompletedAction(columns, widths, ColumnDefaults.Order));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetColumnWidth(SetColumnWidthAction action, IDispatcher dispatcher)
    {
        // Read from post-reducer state to avoid race conditions
        _preferencesProvider.ColumnWidthsPreference = new Dictionary<ColumnName, int>(_logTableState.Value.ColumnWidths);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleToggleColumn(ToggleColumnAction action, IDispatcher dispatcher)
    {
        var columns = new Dictionary<ColumnName, bool>();
        var enabledColumns = _preferencesProvider.EnabledEventTableColumnsPreference;

        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            columns.Add(column,
                column.Equals(action.ColumnName) ?
                    !enabledColumns.Contains(column) :
                    enabledColumns.Contains(column));
        }

        _preferencesProvider.EnabledEventTableColumnsPreference = columns.Keys.Where(column => columns[column]);

        var widths = BuildWidths();
        var order = BuildOrder();

        dispatcher.Dispatch(new LoadColumnsCompletedAction(columns, widths, order));

        return Task.CompletedTask;
    }

    private ImmutableList<ColumnName> BuildOrder()
    {
        var savedOrder = _preferencesProvider.ColumnOrderPreference.ToList();

        if (savedOrder.Count == 0)
        {
            return ColumnDefaults.Order;
        }

        // Start with saved order, then append any new columns not in saved order
        var allColumns = Enum.GetValues<ColumnName>().ToHashSet();
        var ordered = savedOrder.Where(allColumns.Contains).ToList();
        var missing = ColumnDefaults.Order.Where(c => !ordered.Contains(c));

        return [.. ordered, .. missing];
    }

    private Dictionary<ColumnName, int> BuildWidths()
    {
        var savedWidths = _preferencesProvider.ColumnWidthsPreference;
        var widths = new Dictionary<ColumnName, int>();

        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            widths[column] = savedWidths.TryGetValue(column, out int width) ? width : ColumnDefaults.GetWidth(column);
        }

        return widths;
    }
}

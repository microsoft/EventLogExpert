// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class Effects(
    ILogTablePreferencesProvider preferencesProvider,
    IState<LogTableState> logTableState,
    ILogTableColumnDefaultsProvider columnDefaults,
    IColumnResetMigrator columnResetMigrator,
    GroupCollapseNotifier groupCollapseNotifier)
{
    private readonly ILogTableColumnDefaultsProvider _columnDefaults = columnDefaults;
    private readonly IColumnResetMigrator _columnResetMigrator = columnResetMigrator;
    private readonly GroupCollapseNotifier _groupCollapseNotifier = groupCollapseNotifier;
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly ILogTablePreferencesProvider _preferencesProvider = preferencesProvider;

    [EffectMethod(typeof(LoadColumnsAction))]
    public Task HandleLoadColumns(IDispatcher dispatcher)
    {
        if (_columnResetMigrator.ShouldRunMigration()) { _columnResetMigrator.RunMigration(); }

        var columns = new Dictionary<ColumnName, bool>();
        var enabledColumns = _preferencesProvider.EnabledEventTableColumnsPreference;

        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            columns.Add(column, enabledColumns.Contains(column));
        }

        var widths = BuildWidths();
        var order = BuildOrder();

        dispatcher.Dispatch(new LoadColumnsCompletedAction(columns.ToImmutableDictionary(), widths.ToImmutableDictionary(), order));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleReorderColumn(ReorderColumnAction action, IDispatcher dispatcher)
    {
        _preferencesProvider.ColumnOrderPreference = _logTableState.Value.ColumnOrder;

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ResetColumnDefaultsAction))]
    public Task HandleResetColumnDefaults(IDispatcher dispatcher)
    {
        var columns = new Dictionary<ColumnName, bool>();

        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            columns.Add(column, _columnDefaults.EnabledColumns.Contains(column));
        }

        _preferencesProvider.EnabledEventTableColumnsPreference = _columnDefaults.EnabledColumns;
        _preferencesProvider.ColumnWidthsPreference = new Dictionary<ColumnName, int>();
        _preferencesProvider.ColumnOrderPreference = [];

        var widths = new Dictionary<ColumnName, int>(_columnDefaults.ColumnWidths);

        dispatcher.Dispatch(new LoadColumnsCompletedAction(columns.ToImmutableDictionary(), widths.ToImmutableDictionary(), _columnDefaults.ColumnOrder));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(SetAllGroupsCollapsedAction))]
    public Task HandleSetAllGroupsCollapsed(IDispatcher dispatcher)
    {
        _groupCollapseNotifier.Raise();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetColumnWidth(SetColumnWidthAction action, IDispatcher dispatcher)
    {
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

        dispatcher.Dispatch(new LoadColumnsCompletedAction(columns.ToImmutableDictionary(), widths.ToImmutableDictionary(), order));

        return Task.CompletedTask;
    }

    private ImmutableList<ColumnName> BuildOrder()
    {
        var savedOrder = _preferencesProvider.ColumnOrderPreference.ToList();

        if (savedOrder.Count == 0)
        {
            return _columnDefaults.ColumnOrder;
        }

        var allColumns = Enum.GetValues<ColumnName>().ToHashSet();
        var ordered = savedOrder.Where(allColumns.Contains).ToList();
        var missing = _columnDefaults.ColumnOrder.Where(c => !ordered.Contains(c));

        return [.. ordered, .. missing];
    }

    private Dictionary<ColumnName, int> BuildWidths()
    {
        var savedWidths = _preferencesProvider.ColumnWidthsPreference;
        var widths = new Dictionary<ColumnName, int>();

        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            widths[column] = savedWidths.TryGetValue(column, out int width) ? width : _columnDefaults.GetColumnWidth(column);
        }

        return widths;
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public sealed partial class TableColumnMenu
{
    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; init; } = null!;

    [Inject]
    private IStateSelection<SettingsState, IImmutableDictionary<ColumnName, bool>>
        EventTableColumnsState { get; init; } = null!;

    protected override void OnInitialized()
    {
        EventTableColumnsState.Select(s => s.EventTableColumns);

        base.OnInitialized();
    }

    private void OrderColumn(ColumnName columnName) => Dispatcher.Dispatch(new EventTableAction.SetOrderBy(columnName));

    private void ToggleColumn(ColumnName columnName)
    {
        Dispatcher.Dispatch(new SettingsAction.ToggleColumn(columnName));
        Dispatcher.Dispatch(new SettingsAction.LoadColumns());
    }
}

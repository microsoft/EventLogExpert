// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventTable;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public sealed partial class TableColumnMenu
{
    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject]
    private IStateSelection<EventTableState, IImmutableDictionary<ColumnName, bool>>
        EventTableColumnsState { get; init; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; init; } = null!;

    protected override void OnInitialized()
    {
        EventTableColumnsState.Select(s => s.Columns);

        base.OnInitialized();
    }

    private static void HandleActivationKey(KeyboardEventArgs args, Action action)
    {
        // Ignore auto-repeat so holding Enter/Space doesn't dispatch the action
        // repeatedly while the key is held down.
        if (args.Repeat)
        {
            return;
        }

        if (args.Key is "Enter" or " ")
        {
            action();
        }
    }

    private void OrderColumn(ColumnName columnName) => Dispatcher.Dispatch(new EventTableAction.SetOrderBy(columnName));

    private void ResetDefaults() => Dispatcher.Dispatch(new EventTableAction.ResetColumnDefaults());

    private void ToggleColumn(ColumnName columnName) =>
        Dispatcher.Dispatch(new EventTableAction.ToggleColumn(columnName));
}

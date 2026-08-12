// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.LogTable.OrderedView;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class OrderedViewSource : IOrderedViewSource, IDisposable
{
    private readonly Lock _gate = new();
    private readonly IState<LogTableState> _logTableState;
    private readonly ITraceLogger _logger;

    private OrderedViewPresentation _current;
    private bool _disposed;

    public OrderedViewSource(
        IState<LogTableState> logTableState,
        [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logTableState);
        ArgumentNullException.ThrowIfNull(logger);

        _logTableState = logTableState;
        _logger = logger;

        _current = Project(logTableState.Value, revision: 0);
        _logTableState.StateChanged += OnStateChanged;

        lock (_gate)
        {
            var reconciled = Project(_logTableState.Value, _current.Revision + 1);

            if (!IsEqual(_current, reconciled)) { _current = reconciled; }
        }
    }

    public event Action<OrderedViewPresentation>? Updated;

    public OrderedViewPresentation Current
    {
        get { lock (_gate) { return _current; } }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;
        }

        _logTableState.StateChanged -= OnStateChanged;
    }

    private static bool IsEqual(OrderedViewPresentation current, OrderedViewPresentation next) =>
        ReferenceEquals(current.View, next.View) &&
        current.ContentToken == next.ContentToken &&
        current.ActiveTabId == next.ActiveTabId &&
        current.Ordering == next.Ordering &&
        current.State == next.State &&
        current.FaultCause == next.FaultCause &&
        current.OrderingIsStale == next.OrderingIsStale &&
        current.GroupsCollapsedByDefault == next.GroupsCollapsedByDefault &&
        current.ActiveLogName == next.ActiveLogName &&
        (ReferenceEquals(current.GroupCollapseOverrides, next.GroupCollapseOverrides) ||
            current.GroupCollapseOverrides.SetEquals(next.GroupCollapseOverrides)) &&
        ReferenceEquals(current.Columns, next.Columns) &&
        ReferenceEquals(current.ColumnOrder, next.ColumnOrder) &&
        ReferenceEquals(current.ColumnWidths, next.ColumnWidths);

    private static OrderedViewPresentation Project(LogTableState state, long revision)
    {
        var activeTable = state.EventTables.FirstOrDefault(table => table.Id == state.ActiveEventLogId);

        var ordering = new DisplayOrdering(state.OrderBy, state.IsDescending, state.GroupBy, state.IsGroupDescending);

        IEventColumnView view = activeTable is null ?
            LogTableState.EmptyView :
            state.DisplayedEventsForTab(activeTable);

        var presentationState = state.PresentationState;

        return new OrderedViewPresentation(
            view,
            activeTable?.Id,
            ordering,
            presentationState,
            revision,
            presentationState == PresentationState.Faulted ? state.FaultCause : null,
            state.OrderingIsStale)
        {
            ActiveLogName = activeTable is { IsCombined: false } ? activeTable.LogName : null,
            GroupsCollapsedByDefault = state.GroupsCollapsedByDefault,
            ContentToken = activeTable is null ? ViewContentToken.Empty : state.ContentTokenForTab(activeTable),
            GroupCollapseOverrides = state.GroupCollapseOverrides,
            Columns = state.Columns,
            ColumnOrder = state.ColumnOrder,
            ColumnWidths = state.ColumnWidths
        };
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        OrderedViewPresentation published;

        lock (_gate)
        {
            if (_disposed) { return; }

            OrderedViewPresentation next = Project(_logTableState.Value, _current.Revision + 1);

            if (IsEqual(_current, next)) { return; }

            _current = next;
            published = next;
        }

        Publish(published);
    }

    private void Publish(OrderedViewPresentation presentation)
    {
        Action<OrderedViewPresentation>? handlers = Updated;

        if (handlers is null) { return; }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<OrderedViewPresentation>)handler)(presentation);
            }
            catch (Exception fault)
            {
                _logger.Trace($"{nameof(OrderedViewSource)}: a subscriber threw and was isolated: {fault}");
            }
        }
    }
}

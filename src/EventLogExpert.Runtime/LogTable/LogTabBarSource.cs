// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class LogTabBarSource : ILogTabBarSource, IDisposable
{
    private readonly Lock _gate = new();
    private readonly IState<LogTableState> _logTableState;
    private readonly ITraceLogger _logger;
    private readonly IState<FilteredLogPresenceState> _presenceState;

    private LogTabBarPresentation _current;
    private bool _disposed;

    public LogTabBarSource(
        IState<LogTableState> logTableState,
        IState<FilteredLogPresenceState> presenceState,
        [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logTableState);
        ArgumentNullException.ThrowIfNull(presenceState);
        ArgumentNullException.ThrowIfNull(logger);

        _logTableState = logTableState;
        _presenceState = presenceState;
        _logger = logger;

        _current = Project(logTableState.Value, presenceState.Value);
        _logTableState.StateChanged += OnStateChanged;
        _presenceState.StateChanged += OnStateChanged;

        lock (_gate) { _current = Project(_logTableState.Value, _presenceState.Value); }
    }

    public event Action? Changed;

    public LogTabBarPresentation Current
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
        _presenceState.StateChanged -= OnStateChanged;
    }

    private static ImmutableHashSet<EventLogId> ComputeKnownEmptyTabIds(
        LogTableState logTable,
        FilteredLogPresenceState presence)
    {
        var builder = ImmutableHashSet.CreateBuilder<EventLogId>();

        foreach (var table in logTable.EventTables)
        {
            if (table.IsCombined || table.IsLoading) { continue; }

            if (presence.IsKnownEmpty(table.Id)) { builder.Add(table.Id); }
        }

        return builder.ToImmutable();
    }

    private static bool IsEqual(LogTabBarPresentation next, LogTabBarPresentation current) =>
        ReferenceEquals(next.Tabs, current.Tabs) &&
        ReferenceEquals(next.Groups, current.Groups) &&
        next.ActiveTabId == current.ActiveTabId &&
        next.KnownEmptyTabIds.SetEquals(current.KnownEmptyTabIds);

    private static LogTabBarPresentation Project(LogTableState logTable, FilteredLogPresenceState presence) =>
        new()
        {
            Tabs = logTable.EventTables,
            Groups = logTable.Groups,
            ActiveTabId = logTable.ActiveEventLogId,
            KnownEmptyTabIds = ComputeKnownEmptyTabIds(logTable, presence)
        };

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var next = Project(_logTableState.Value, _presenceState.Value);

        lock (_gate)
        {
            if (_disposed || IsEqual(next, _current)) { return; }

            _current = next;
        }

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var handlers = Changed;

        if (handlers is null) { return; }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception fault)
            {
                _logger.Trace($"{nameof(LogTabBarSource)}: a subscriber threw and was isolated: {fault}");
            }
        }
    }
}

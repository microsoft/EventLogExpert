// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using Fluxor;
using NSubstitute;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

internal sealed class OrderedViewShadowHarness : IAsyncDisposable
{
    private EventLogState _eventLog = new();
    private LogTableState _logTable = new();
    private RawEventStoreState _rawStore = new();

    public OrderedViewShadowHarness()
    {
        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(_ => _logTable);

        var rawEventStore = Substitute.For<IState<RawEventStoreState>>();
        rawEventStore.Value.Returns(_ => _rawStore);

        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(_ => _eventLog);

        Bridge = new OrderedViewDispatchBridge(Dispatcher, Writer);

        Effects = new OrderedViewShadowEffects(eventLogState,
            logTableState,
            rawEventStore,
            Writer,
            Issuer,
            Bridge,
            Dispatcher,
            ConcurrencyState,
            MatchCache);
    }

    public OrderedViewDispatchBridge Bridge { get; }

    public EventLogConcurrencyState ConcurrencyState { get; } = new();

    public IDispatcher Dispatcher { get; } = Substitute.For<IDispatcher>();

    public OrderedViewShadowEffects Effects { get; }

    public ViewRequestIssuer Issuer { get; } = new();

    public XmlFilterMatchCache MatchCache { get; } = new();

    public OrderedViewWriter Writer { get; } = new(publishIntervalMs: 0);

    public async ValueTask DisposeAsync()
    {
        Bridge.Dispose();
        await Writer.DisposeAsync();
    }

    public async Task<OrderedViewUpdate> DrainToUpdateAsync(CancellationToken cancellationToken)
    {
        OrderedViewUpdate? latest = null;
        Lock gate = new();

        void Capture(OrderedViewUpdate update)
        {
            lock (gate) { latest = update; }
        }

        Writer.Updated += Capture;

        try
        {
            await Writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);
            await Writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);
        }
        finally
        {
            Writer.Updated -= Capture;
        }

        lock (gate)
        {
            return latest ?? throw new InvalidOperationException("The writer raised no update.");
        }
    }

    public void SetState(LogTableState logTable, RawEventStoreState rawStore, EventLogState eventLog)
    {
        _logTable = logTable;
        _rawStore = rawStore;
        _eventLog = eventLog;
    }
}

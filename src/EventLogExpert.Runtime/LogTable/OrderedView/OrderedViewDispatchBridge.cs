// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class OrderedViewDispatchBridge : IDisposable
{
    private readonly IDispatcher _dispatcher;
    private readonly OrderedViewWriter _writer;

    public OrderedViewDispatchBridge(IDispatcher dispatcher, OrderedViewWriter writer)
    {
        _dispatcher = dispatcher;
        _writer = writer;
        _writer.Updated += OnUpdated;
        _writer.FaultRaised += OnFaultRaised;
    }

    public void Dispose()
    {
        _writer.Updated -= OnUpdated;
        _writer.FaultRaised -= OnFaultRaised;
    }

    public void NotifyShadowFault(Exception fault) => _dispatcher.Dispatch(new OrderedViewDisplayFaultedAction(fault));

    private void OnFaultRaised(Exception fault, ViewIdentity? identity) =>
        _dispatcher.Dispatch(new OrderedViewDisplayFaultedAction(fault, identity));

    private void OnUpdated(OrderedViewUpdate update) =>
        _dispatcher.Dispatch(new OrderedViewUpdatedAction(update));
}

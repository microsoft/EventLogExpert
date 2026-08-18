// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.ActivityCorrelation;

internal sealed class ActivityCorrelationSource : IActivityCorrelationSource, IDisposable
{
    private readonly ITraceLogger _logger;
    private readonly IState<RawEventStoreState> _rawStore;

    private bool _disposed;

    public ActivityCorrelationSource(
        IState<RawEventStoreState> rawStore,
        [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(rawStore);
        ArgumentNullException.ThrowIfNull(logger);

        _rawStore = rawStore;
        _logger = logger;
        _rawStore.StateChanged += OnRawStoreChanged;
    }

    public event Action? Changed;

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        _rawStore.StateChanged -= OnRawStoreChanged;
    }

    private void OnRawStoreChanged(object? sender, EventArgs e)
    {
        var handlers = Changed;

        if (_disposed || handlers is null) { return; }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch (Exception fault)
            {
                _logger.Trace($"{nameof(ActivityCorrelationSource)}: a subscriber threw and was isolated: {fault}");
            }
        }
    }
}

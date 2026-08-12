// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class SetFilterDateRangeSucceededNotifier : ISetFilterDateRangeSucceededNotifier
{
    private readonly ITraceLogger _logger;

    public SetFilterDateRangeSucceededNotifier([FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public event Action? Succeeded;

    public void Raise()
    {
        var handlers = Succeeded;

        if (handlers is null) { return; }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception fault)
            {
                _logger.Trace($"{nameof(SetFilterDateRangeSucceededNotifier)}: a subscriber threw and was isolated: {fault}");
            }
        }
    }
}

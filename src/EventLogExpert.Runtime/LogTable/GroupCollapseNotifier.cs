// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class GroupCollapseNotifier : IGroupCollapseNotifier
{
    private readonly ITraceLogger _logger;

    public GroupCollapseNotifier([FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public event Action? Requested;

    public void Raise()
    {
        var handlers = Requested;

        if (handlers is null) { return; }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception fault)
            {
                _logger.Trace($"{nameof(GroupCollapseNotifier)}: a subscriber threw and was isolated: {fault}");
            }
        }
    }
}

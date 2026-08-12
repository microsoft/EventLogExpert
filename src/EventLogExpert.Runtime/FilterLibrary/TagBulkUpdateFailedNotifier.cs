// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class TagBulkUpdateFailedNotifier : ITagBulkUpdateFailedNotifier
{
    private readonly ITraceLogger _logger;

    public TagBulkUpdateFailedNotifier([FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public event Action? Failed;

    public void Raise()
    {
        var handlers = Failed;

        if (handlers is null) { return; }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception fault)
            {
                _logger.Trace($"{nameof(TagBulkUpdateFailedNotifier)}: a subscriber threw and was isolated: {fault}");
            }
        }
    }
}

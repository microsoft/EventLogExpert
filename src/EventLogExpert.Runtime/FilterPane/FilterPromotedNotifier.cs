// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.FilterPane;

/// <summary>
///     Raised by the FilterPane commit effect after a lens is promoted into a persistent filter, so the FilterPane
///     component can expand its (otherwise collapsed) filter list to reveal the newly kept row. Mirrors
///     <see cref="SetFilterDateRangeSucceededNotifier" />: the concrete type raises, the interface subscribes, and one
///     shared instance is registered.
/// </summary>
internal sealed class FilterPromotedNotifier : IFilterPromotedNotifier
{
    private readonly ITraceLogger _logger;

    public FilterPromotedNotifier([FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public event Action? Promoted;

    public void Raise()
    {
        var handlers = Promoted;

        if (handlers is null) { return; }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception fault)
            {
                _logger.Trace($"{nameof(FilterPromotedNotifier)}: a subscriber threw and was isolated: {fault}");
            }
        }
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Modal;

namespace EventLogExpert.Runtime.Alerts;

internal sealed class InlineAlertHostBroker(IModalService modalService) : IInlineAlertHostBroker
{
    private readonly IModalService _modalService = modalService;
    private readonly Lock _stateLock = new();

    private long _activeId;
    private IInlineAlertHost? _host;

    public void Register(long modalId, IInlineAlertHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (_stateLock)
        {
            // Late registration from a stale modal would route alerts to a torn-down host.
            if (modalId != _modalService.ActiveModalId) { return; }

            _host = host;
            _activeId = modalId;
        }
    }

    public bool TryGet(out IInlineAlertHost? host)
    {
        lock (_stateLock)
        {
            // Lazy invalidation: if the active modal has changed since the last register/unregister, the recorded
            // host belongs to a torn-down modal. Drop it instead of routing the alert there.
            if (_activeId != _modalService.ActiveModalId)
            {
                _host = null;
                _activeId = 0;
            }

            host = _host;
            return host is not null;
        }
    }

    public void Unregister(long modalId)
    {
        lock (_stateLock)
        {
            if (modalId != _activeId) { return; }

            _host = null;
            _activeId = 0;
        }
    }
}

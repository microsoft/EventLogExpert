// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Alerts;

/// <summary>
///     Tracks the currently registered <see cref="IInlineAlertHost" /> so cross-cutting alert dispatchers can route
///     inline alerts to the active modal without forcing the Modal/ slice to know about the Alerts/ slice. Stale
///     registrations (from modals that have already been replaced) are rejected so a torn-down host never receives alerts.
/// </summary>
public interface IInlineAlertHostBroker
{
    /// <summary>
    ///     Register <paramref name="host" /> as the active inline-alert host for the modal identified by
    ///     <paramref name="modalId" />. Stale ids (from replaced modals) are ignored.
    /// </summary>
    void Register(long modalId, IInlineAlertHost host);

    /// <summary>
    ///     Returns the currently registered inline-alert host, if any. Inspect on every alert because the active modal
    ///     may have changed since the last call.
    /// </summary>
    bool TryGet(out IInlineAlertHost? host);

    /// <summary>
    ///     Clear the active inline-alert host. Stale ids (from modals that no longer own the registration) are ignored so
    ///     a late teardown cannot wipe a successor's host.
    /// </summary>
    void Unregister(long modalId);
}

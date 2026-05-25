// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.Modal;

/// <summary>Coordinates modal lifecycle and owns the inline-alert host registry that callers route through.</summary>
public interface IModalCoordinator
{
    event Action? StateChanged;

    ModalSession? ActiveSession { get; }

    void Complete<TResult>(ModalId modalId, TResult? result);

    void ForceCloseActive();

    /// <summary>Returns the active modal's scope, or <see langword="null" /> if no modal is active.</summary>
    ModalScope? GetActiveModalScope();

    /// <summary>
    ///     Opens <typeparamref name="TModal" />. If an active modal exists, asks it to close via the veto pipeline first;
    ///     if vetoed, returns a result with <see cref="ModalOpenResult{TResult}.WasOpened" /> set to <see langword="false" />.
    /// </summary>
    Task<ModalOpenResult<TResult>> PushAsync<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent;

    /// <summary>Register a modal's close handler, scope, and optional inline-alert host. Stale ids are ignored.</summary>
    void RegisterModal(ModalRegistration registration);

    /// <summary>
    ///     Asks the active modal to close. Coalesces concurrent calls; the first verdict wins. Critical-scoped modals
    ///     reject <see cref="ModalCloseReason.OtherModalActivation" /> immediately, regardless of in-flight state.
    ///     If a close handler throws <see cref="OperationCanceledException" /> (e.g., from a force-close race), the
    ///     close is treated as accepted so coalesced awaiters resolve successfully.
    /// </summary>
    Task<bool> RequestCloseActiveAsync(ModalCloseReason reason);

    bool TryGetInlineAlertHost([NotNullWhen(true)] out IInlineAlertHost? host);

    void UnregisterModal(ModalId modalId);
}


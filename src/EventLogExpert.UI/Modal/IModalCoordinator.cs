// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Alerts;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.UI.Modal;

public interface IModalCoordinator
{
    event Action? StateChanged;

    ModalSession? ActiveSession { get; }

    void Complete<TResult>(ModalId modalId, TResult? result);

    void ForceCloseActive();

    ModalScope? GetActiveModalScope();

    Task<ModalOpenResult<TResult>> PushAsync<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent;

    void RegisterModal(ModalRegistration registration);

    Task<bool> RequestCloseActiveAsync(ModalCloseReason reason);

    bool TryGetInlineAlertHost([NotNullWhen(true)] out IInlineAlertHost? host);

    void UnregisterModal(ModalId modalId);
}


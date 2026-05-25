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

    Task<TResult?> PushAsync<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent;

    /// <summary>
    ///     Register <paramref name="host" /> as the inline-alert host for the modal identified by
    ///     <paramref name="modalId" />. Stale ids are ignored.
    /// </summary>
    void RegisterInlineAlertHost(ModalId modalId, IInlineAlertHost host);

    /// <summary>Returns the registered inline-alert host for the active modal, if any.</summary>
    bool TryGetInlineAlertHost([NotNullWhen(true)] out IInlineAlertHost? host);

    void UnregisterInlineAlertHost(ModalId modalId);
}


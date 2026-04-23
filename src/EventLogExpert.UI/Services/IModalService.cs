// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Services;

/// <summary>
///     Coordinates a single active modal at a time. Hosts subscribe to <see cref="StateChanged" /> and render
///     <see cref="ActiveModalType" /> with <see cref="ActiveModalParameters" />.
/// </summary>
public interface IModalService
{
    event Action? StateChanged;

    /// <summary>Per-show id; use as a <c>@key</c> so reopening produces a fresh component instance.</summary>
    long ActiveModalId { get; }

    IDictionary<string, object?>? ActiveModalParameters { get; }

    Type? ActiveModalType { get; }

    void CancelActive();

    /// <summary>Complete the active modal's task. Stale ids (from replaced modals) are ignored.</summary>
    void Complete<TResult>(long modalId, TResult? result);

    /// <summary>Register the active modal as the inline-alert host. Stale ids are ignored.</summary>
    void RegisterActiveAlertHost(long modalId, IInlineAlertHost host);

    /// <summary>Open a modal. Any prior active modal is canceled (its task completes with default).</summary>
    Task<TResult?> Show<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent;

    /// <summary>Returns the currently registered alert host, if any. Inspect on every alert.</summary>
    bool TryGetActiveAlertHost(out IInlineAlertHost? host);

    void UnregisterActiveAlertHost(long modalId);
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

/// <summary>
/// Coordinates a single active modal at a time. The host UI subscribes to <see cref="StateChanged"/>
/// and renders <see cref="ActiveModalType"/> with <see cref="ActiveModalParameters"/>.
/// Modals call <see cref="Complete{TResult}"/> with the id they captured at construction time.
/// </summary>
public interface IModalService
{
    event Action? StateChanged;

    /// <summary>Monotonically increasing per-show identifier. Use as a `@key` so reopening the same
    /// modal type produces a fresh component instance.</summary>
    long ActiveModalId { get; }

    IDictionary<string, object?>? ActiveModalParameters { get; }

    Type? ActiveModalType { get; }

    /// <summary>Cancel any in-flight modal (e.g., on app shutdown).</summary>
    void CancelActive();

    /// <summary>Complete the active modal's pending task. Stale ids (from previously replaced
    /// modals) are silently ignored, preventing late callbacks from completing the wrong task.</summary>
    void Complete<TResult>(long modalId, TResult? result);

    /// <summary>Register the active modal as the inline-alert host so the alert dialog service can
    /// route alerts inline. Callers must use the same <paramref name="modalId"/> they captured when
    /// becoming active; stale ids are ignored.</summary>
    void RegisterActiveAlertHost(long modalId, IInlineAlertHost host);

    /// <summary>Open a modal. If another modal is already active, its pending result is completed
    /// with <c>default</c> before the new modal becomes active. Returns a task that completes when
    /// the new modal closes.</summary>
    Task<TResult?> Show<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : Microsoft.AspNetCore.Components.IComponent;

    /// <summary>Returns the currently registered inline-alert host (if any). The host is only
    /// available between <see cref="RegisterActiveAlertHost"/> and <see cref="UnregisterActiveAlertHost"/>
    /// (or until the active modal changes), so callers must inspect the result on every alert.</summary>
    bool TryGetActiveAlertHost(out IInlineAlertHost? host);

    /// <summary>Unregister an inline-alert host. Stale ids are ignored.</summary>
    void UnregisterActiveAlertHost(long modalId);
}

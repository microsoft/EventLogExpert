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

    /// <summary>Open a modal. If another modal is already active, its pending result is completed
    /// with <c>default</c> before the new modal becomes active. Returns a task that completes when
    /// the new modal closes.</summary>
    Task<TResult?> Show<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : Microsoft.AspNetCore.Components.IComponent;
}

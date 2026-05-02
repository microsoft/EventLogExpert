// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

/// <summary>
///     Singleton aggregator for the app-level banner surface. Holds the current critical exception (highest priority,
///     non-dismissible), the queue of error banners (FIFO, individually dismissible), and the queue of info banners
///     (FIFO, individually dismissible). The banner host renders one card at a time by priority: critical &gt; error
///     &gt; info. State is thread-safe; mutations raise <see cref="StateChanged" /> after the lock is released so handlers
///     do not run under the service lock.
/// </summary>
public interface IBannerService
{
    event Action StateChanged;

    /// <summary>
    ///     Snapshot of the currently-running background-scope upgrade batch (e.g., import-triggered auto-upgrades), or
    ///     <c>null</c> when no background batch is in flight. At most one of <see cref="BackgroundProgress" /> and
    ///     <see cref="SettingsProgress" /> is non-null at any time because the database service processes batches
    ///     sequentially, but the two slots are kept separate so the top-level banner host and the inline settings banner
    ///     can each query their own slot without needing scope-discrimination logic.
    /// </summary>
    BannerProgressEntry? BackgroundProgress { get; }

    Exception? CurrentCritical { get; }

    IReadOnlyList<ErrorBannerEntry> ErrorBanners { get; }

    IReadOnlyList<BannerInfoEntry> InfoBanners { get; }

    /// <summary>
    ///     Snapshot of the currently-running settings-scope upgrade batch (triggered from the SettingsModal toggle
    ///     confirmation), or <c>null</c> when no settings batch is in flight. See <see cref="BackgroundProgress" /> for
    ///     scope-routing rationale.
    /// </summary>
    BannerProgressEntry? SettingsProgress { get; }

    /// <summary>Clear the current critical exception and raise <see cref="StateChanged" />.</summary>
    void ClearCritical();

    /// <summary>Remove an error banner by id and raise <see cref="StateChanged" />. No-op if the id is not present.</summary>
    void DismissError(Guid id);

    /// <summary>Remove an info banner by id and raise <see cref="StateChanged" />. No-op if the id is not present.</summary>
    void DismissInfoBanner(Guid id);

    /// <summary>
    ///     Register a callback that <see cref="TryRecoverAsync" /> invokes before clearing the critical exception. Replaces
    ///     any prior registration immediately. Dispose the returned handle to unregister; disposal is idempotent and a no-op
    ///     if a newer registration has already replaced this one.
    /// </summary>
    IDisposable RegisterRecoveryCallback(Func<Task> recover);

    /// <summary>Replace the current critical exception and raise <see cref="StateChanged" />.</summary>
    void ReportCritical(Exception ex);

    /// <summary>
    ///     Append an error banner to the queue and raise <see cref="StateChanged" />. Returns the new entry's id so callers
    ///     can later call <see cref="DismissError" /> when the underlying issue is resolved. <paramref name="actionLabel" />
    ///     and <paramref name="action" /> are optional; when both are provided the banner renders an action button between
    ///     the message and the dismiss button. Providing exactly one (or providing an action with a whitespace label)
    ///     throws <see cref="ArgumentException" /> — partial action state is a caller bug. The action callback owns its
    ///     own cleanup (e.g., dismissing the banner after the user resolves the issue).
    /// </summary>
    Guid ReportError(string title, string message, string? actionLabel = null, Func<Task>? action = null);

    /// <summary>Append an info banner to the queue and raise <see cref="StateChanged" />.</summary>
    void ReportInfoBanner(string title, string message, BannerSeverity severity);

    /// <summary>
    ///     Invoke the registered recovery callback (if any), then clear the critical exception. If no callback is
    ///     registered, just clears the exception.
    /// </summary>
    Task TryRecoverAsync();
}

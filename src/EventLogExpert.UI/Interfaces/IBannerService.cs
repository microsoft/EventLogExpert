// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

/// <summary>
///     Singleton aggregator for the app-level banner surface. Holds the current unhandled error (highest priority,
///     non-dismissible), the queue of critical alerts (FIFO, individually dismissible), and the queue of info banners
///     (FIFO, individually dismissible). The banner host renders one card at a time by priority: error &gt; critical
///     &gt; info. State is thread-safe; mutations raise <see cref="StateChanged" /> after the lock is released so handlers
///     do not run under the service lock.
/// </summary>
public interface IBannerService
{
    event Action StateChanged;

    IReadOnlyList<CriticalAlertEntry> CriticalAlerts { get; }

    IReadOnlyList<BannerInfoEntry> InfoBanners { get; }

    Exception? UnhandledError { get; }

    /// <summary>Clear the current unhandled error and raise <see cref="StateChanged" />.</summary>
    void ClearError();

    /// <summary>Remove a critical alert by id and raise <see cref="StateChanged" />. No-op if the id is not present.</summary>
    void DismissCritical(Guid id);

    /// <summary>Remove an info banner by id and raise <see cref="StateChanged" />. No-op if the id is not present.</summary>
    void DismissInfoBanner(Guid id);

    /// <summary>Append a critical alert to the queue and raise <see cref="StateChanged" />.</summary>
    void ReportCritical(string title, string message);

    /// <summary>Replace the current unhandled error and raise <see cref="StateChanged" />.</summary>
    void ReportError(Exception ex);

    /// <summary>Append an info banner to the queue and raise <see cref="StateChanged" />.</summary>
    void ReportInfoBanner(string title, string message, BannerSeverity severity);

    /// <summary>
    ///     Register a callback that <see cref="TryRecoverAsync" /> invokes before clearing the error. Replacing an existing
    ///     callback overwrites the prior one. Pass <see langword="null" /> to unregister.
    /// </summary>
    void SetRecoveryCallback(Func<Task>? recover);

    /// <summary>
    ///     Invoke the registered recovery callback (if any), then clear the error. If no callback is registered, just clears
    ///     the error.
    /// </summary>
    Task TryRecoverAsync();
}

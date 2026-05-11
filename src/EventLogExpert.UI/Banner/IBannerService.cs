// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Database;

namespace EventLogExpert.UI.Banner;

public interface IBannerService
{
    event Action StateChanged;

    bool AttentionDismissed { get; }

    IReadOnlyList<DatabaseEntry> AttentionEntries { get; }

    BannerProgressEntry? BackgroundProgress { get; }

    Exception? CurrentCritical { get; }

    IReadOnlyList<ErrorBannerEntry> ErrorBanners { get; }

    IReadOnlyList<BannerInfoEntry> InfoBanners { get; }

    BannerProgressEntry? SettingsProgress { get; }

    void ClearCritical();

    void DismissAttention();

    void DismissError(Guid id);

    /// <summary>Remove an info banner by id and raise <see cref="StateChanged" />. No-op if the id is not present.</summary>
    void DismissInfoBanner(Guid id);

    IDisposable RegisterRecoveryCallback(Func<Task> recover);

    void ReportCritical(Exception ex);

    Guid ReportError(string title, string message, string? actionLabel = null, Func<Task>? action = null);

    void ReportInfoBanner(string title, string message, BannerSeverity severity);

    Task TryRecoverAsync();
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

/// <summary>
///     Tracks the single in-flight event export so the banner cycle can surface an indeterminate, cancelable progress
///     banner. This is deliberately separate from the database-coupled <c>BannerService</c> facets: export progress has no
///     upgrade/database dependency.
/// </summary>
public interface IExportProgressBannerService
{
    event Action StateChanged;

    ExportProgressEntry? CurrentExport { get; }

    void Begin(Action cancel);

    /// <summary>Clears the current export entry. Idempotent when no export is in progress.</summary>
    void End();
}

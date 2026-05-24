// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;

namespace EventLogExpert.Runtime.DatabaseTools;

/// <summary>
///     Runtime-level facade for the five DatabaseTools operations (Show / Create / Merge / Diff / Upgrade). Each
///     method dispatches the matching <see cref="EventLogExpert.DatabaseTools.Operations.IDatabaseToolsOperation" /> on a
///     worker thread, streams log entries via <paramref name="logSink" /> as they are emitted (not batched at end), and
///     returns the final outcome + duration. Cancellation is cooperative — see the Operation contract for the per-step
///     cancellation semantics.
/// </summary>
/// <remarks>
///     The <c>verbose</c> parameter mirrors EventDbTool's <c>--verbose</c> CLI flag: when <c>false</c> (default), the
///     streaming logger filters to <c>LogLevel.Information</c> and above so UI consumers only see narrative output; when
///     <c>true</c>, it lowers the threshold to <c>LogLevel.Trace</c> so the full diagnostic stream surfaces.
/// </remarks>
public interface IDatabaseToolsService
{
    Task<DatabaseToolsResult> CreateAsync(
        CreateDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> DiffAsync(
        DiffDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> MergeAsync(
        MergeDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> ShowAsync(
        ShowProvidersRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> UpgradeAsync(
        UpgradeDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);
}

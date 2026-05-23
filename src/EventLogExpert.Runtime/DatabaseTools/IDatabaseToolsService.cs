// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;

namespace EventLogExpert.Runtime.DatabaseTools;

/// <summary>
///     Runtime-level facade for the five DatabaseTools operations (Show / Create / Merge / Diff / Upgrade). Each
///     method dispatches the matching <see cref="EventLogExpert.DatabaseTools.Operations.IDatabaseToolsOperation"/> on
///     a worker thread, streams log entries via <paramref name="logSink"/> as they are emitted (not batched at end), and
///     returns the final outcome + duration. Cancellation is cooperative — see the Operation contract for the per-step
///     cancellation semantics.
/// </summary>
public interface IDatabaseToolsService
{
    Task<DatabaseToolsResult> ShowAsync(
        ShowProvidersRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken);

    Task<DatabaseToolsResult> CreateAsync(
        CreateDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken);

    Task<DatabaseToolsResult> MergeAsync(
        MergeDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken);

    Task<DatabaseToolsResult> DiffAsync(
        DiffDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken);

    Task<DatabaseToolsResult> UpgradeAsync(
        UpgradeDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken);
}

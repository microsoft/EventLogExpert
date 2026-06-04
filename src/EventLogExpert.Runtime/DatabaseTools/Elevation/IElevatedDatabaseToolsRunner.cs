// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

/// <summary>
///     Runner facade for executing a DatabaseTools operation in an elevated child process. Mirrors the
///     <see cref="IDatabaseToolsService" /> shape so a tab's dispatch site can choose between in-process (medium-IL)
///     execution and elevated (high-IL) execution by selecting one of the two facades — same request types, same outcome
///     record, same streaming-log/progress callbacks.
/// </summary>
/// <remarks>
///     Implementations spawn the packaged elevation helper EXE via <see cref="IElevatedHelperProcessHost" />, forward
///     the request through the named-pipe IPC channel, stream incoming <c>LogEnvelope</c> / <c>ProgressEnvelope</c>
///     records into the caller-supplied <see cref="IProgress{T}" /> sinks, await the terminal <c>ResultEnvelope</c>, and
///     translate UAC-decline into <see cref="DatabaseToolsOutcome.Cancelled" />. Cancellation is cooperative — see
///     <c>ElevatedDatabaseToolsRunner</c>'s remarks for the cancel envelope / 30s grace / force-kill sequence.
/// </remarks>
public interface IElevatedDatabaseToolsRunner
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

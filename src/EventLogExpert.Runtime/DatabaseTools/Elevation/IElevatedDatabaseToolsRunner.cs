// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

public interface IElevatedDatabaseToolsRunner
{
    Task<DatabaseToolsResult> CreateAsync(
        CreateDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> DiffAsync(
        DiffDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<OfflineImageEditionsResult> ListImageEditionsAsync(
        ListOfflineImageEditionsRequest request,
        IProgress<LogRecord> logProgress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> MergeAsync(
        MergeDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> ShowAsync(
        ShowProvidersRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> UpgradeAsync(
        UpgradeDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);
}

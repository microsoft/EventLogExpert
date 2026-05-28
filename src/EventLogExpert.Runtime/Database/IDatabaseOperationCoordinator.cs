// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database.Upgrade;

namespace EventLogExpert.Runtime.Database;

public interface IDatabaseOperationCoordinator
{
    event Action? UpgradeStateChanged;

    bool IsAnyUpgradeInFlight { get; }

    Task ApplyPendingTogglesAsync(
        IReadOnlyCollection<string> fileNames,
        CancellationToken cancellationToken = default);

    Task<ImportOutcome> ImportAsync(
        Func<string, CancellationToken, Task<bool>>? askOverwriteAsync = null,
        CancellationToken cancellationToken = default);

    bool IsUpgradeInFlight(string fileName);

    Task<RemoveOutcome> RemoveDatabaseAsync(
        string fileName,
        Func<bool, CancellationToken, Task<bool>> confirmRemoveAsync,
        CancellationToken cancellationToken = default);

    Task UpgradeDatabaseAsync(
        string fileName,
        UpgradeProgressScope scope = UpgradeProgressScope.ManageDatabasesTriggered,
        CancellationToken cancellationToken = default);
}

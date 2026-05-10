// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record UpgradeBatchResult(
    IReadOnlyList<string> Succeeded,
    IReadOnlyList<string> Cancelled,
    IReadOnlyList<UpgradeFailure> Failed);

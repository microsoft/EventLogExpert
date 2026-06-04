// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.Common.Operations;

/// <summary>
///     Progress report emitted by a DatabaseTools operation. <see cref="Total" /> is null when the operation cannot
///     know the total up front (e.g., .evtx streaming) — the UI renders an indeterminate progress indicator in that case.
/// </summary>
/// <param name="Processed">Items completed so far.</param>
/// <param name="Total">Expected total items, or null if unknown.</param>
/// <param name="CurrentItem">Optional label identifying the item being processed.</param>
public sealed record DatabaseToolsProgress(int Processed, int? Total, string? CurrentItem);

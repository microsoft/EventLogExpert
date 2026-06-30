// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.PublisherMetadata.Offline;

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

/// <summary>
///     Outcome of <see cref="IElevatedDatabaseToolsRunner.ListImageEditionsAsync" />. On
///     <see cref="DatabaseToolsOutcome.Succeeded" />, <see cref="Editions" /> carries the enumerated image editions (whose
///     <see cref="WimImageList.Status" /> further distinguishes a readable WIM from a non-WIM file); otherwise
///     <see cref="Editions" /> is <c>null</c> and <see cref="FailureSummary" /> explains why.
/// </summary>
/// <param name="Outcome">Whether the helper completed, was cancelled, or failed.</param>
/// <param name="Editions">The enumerated editions on success; <c>null</c> otherwise.</param>
/// <param name="FailureSummary">A human-readable explanation when <paramref name="Outcome" /> is not success.</param>
public sealed record OfflineImageEditionsResult(
    DatabaseToolsOutcome Outcome,
    WimImageList? Editions,
    string? FailureSummary);

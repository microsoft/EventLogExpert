// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.ResolutionCoverage;

public enum CoverageStatus
{
    Full,
    Partial,
    None
}

/// <summary>
///     Maps a <see cref="CoverageStatus" /> to its short display label. Shared by the coverage modal and the
///     clipboard formatter so both render identical wording.
/// </summary>
public static class CoverageStatusText
{
    public static string Label(CoverageStatus status) => status switch
    {
        CoverageStatus.Full => "Full",
        CoverageStatus.None => "None",
        _ => "Partial"
    };
}

public sealed record ProviderCoverageRow(string Provider, ProviderResolutionCounts Counts, CoverageStatus Status);

public sealed record ResolutionCoverageReport(
    ProviderResolutionCounts Summary,
    IReadOnlyList<ProviderCoverageRow> Rows);

/// <summary>One event ID's resolution breakdown within a provider (an F6 drill-down row).</summary>
public sealed record EventIdCoverageRow(int EventId, ProviderResolutionCounts Counts);

/// <summary>
///     One severity level's resolution breakdown within a provider (an F8 drill-down row); <see cref="Level" /> is
///     null for the Unknown / absent-level slot.
/// </summary>
public sealed record LevelCoverageRow(SeverityLevel? Level, ProviderResolutionCounts Counts);

/// <summary>
///     The lazily-computed per-provider drill-down: the top unresolved event IDs (capped, with the true
///     <see cref="DistinctUnresolvedEventIdCount" /> so the UI can note "top N of M") plus the unresolved-by-severity
///     breakdown.
/// </summary>
public sealed record ProviderCoverageDetail(
    IReadOnlyList<EventIdCoverageRow> EventIds,
    IReadOnlyList<LevelCoverageRow> Levels,
    int DistinctUnresolvedEventIdCount);

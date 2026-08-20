// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.ResolutionCoverage;

internal sealed class ResolutionCoverageService : IResolutionCoverageService
{
    // The Event-ID drill-down copies at most this many rows into the modal; the header still reports the true distinct
    // count so a capped list reads "top N of M".
    internal const int MaxEventIdRows = 100;

    // Severity breakdown display order: Critical..Verbose (slots 1-5) then Unknown (slot 0).
    private static readonly int[] s_levelDisplayOrder = [1, 2, 3, 4, 5, 0];

    public ResolutionCoverageReport Build(IEventColumnView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);

        var counts = new Dictionary<string, ProviderResolutionCounts>(StringComparer.Ordinal);
        view.CountResolutionBySource(counts, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        ProviderResolutionCounts summary = default;

        foreach (ProviderResolutionCounts value in counts.Values)
        {
            summary = summary.Add(value);
        }

        var rows = counts
            .OrderByDescending(entry => entry.Value.Unresolved)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ProviderCoverageRow(entry.Key, entry.Value, Classify(entry.Value)))
            .ToList();

        return new ResolutionCoverageReport(summary, rows);
    }

    public ProviderCoverageDetail BuildProviderDetail(IEventColumnView view, string provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(provider);

        var byId = new Dictionary<int, ProviderResolutionCounts>();
        var byLevelSlot = new ProviderResolutionCounts[LevelSeverity.SlotCount];
        view.CountResolutionDetailForSource(provider, byId, byLevelSlot, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var unresolvedIds = byId.Where(entry => entry.Value.Unresolved > 0).ToList();

        var eventIds = unresolvedIds
            .OrderByDescending(entry => entry.Value.Unresolved)
            .ThenByDescending(entry => entry.Value.Total)
            .ThenBy(entry => entry.Key)
            .Take(MaxEventIdRows)
            .Select(entry => new EventIdCoverageRow(entry.Key, entry.Value))
            .ToList();

        var levels = new List<LevelCoverageRow>(LevelSeverity.SlotCount);

        foreach (int slot in s_levelDisplayOrder)
        {
            ProviderResolutionCounts counts = byLevelSlot[slot];

            if (counts.Unresolved == 0) { continue; }

            levels.Add(new LevelCoverageRow(slot == 0 ? null : (SeverityLevel)slot, counts));
        }

        return new ProviderCoverageDetail(eventIds, levels, unresolvedIds.Count);
    }

    private static CoverageStatus Classify(ProviderResolutionCounts counts) =>
        counts.Unresolved == 0 ? CoverageStatus.Full :
        counts.NoProvider == counts.Total ? CoverageStatus.None :
        CoverageStatus.Partial;
}

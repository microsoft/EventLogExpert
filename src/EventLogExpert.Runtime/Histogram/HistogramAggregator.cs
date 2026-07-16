// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public static class HistogramAggregator
{
    private const int MinAnomalyBins = 8;

    public static HistogramRender Aggregate(
        HistogramData data,
        long windowStartTicks,
        long windowEndTicks,
        int targetBins)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetBins, 1);

        long baseMin = data.MinUtc.Ticks;
        long baseMax = data.MaxUtc.Ticks;
        long span = data.BucketSpanTicks;
        int slotCount = data.SlotCount;
        var groups = data.Groups;
        int groupCount = groups.Count;

        // A stale/zoomed window can outrun a shrunk live-tail domain, so clamp it back into the current domain first.
        long startTicks = Math.Clamp(Math.Min(windowStartTicks, windowEndTicks), baseMin, baseMax);
        long endTicks = Math.Clamp(Math.Max(windowStartTicks, windowEndTicks), baseMin, baseMax);

        int binLo = (int)Math.Clamp((startTicks - baseMin) / span, 0, data.BinCount - 1);
        int binHi = (int)Math.Clamp((endTicks - baseMin) / span, 0, data.BinCount - 1);
        int windowBinCount = binHi - binLo + 1;

        int renderBinCount = Math.Min(targetBins, windowBinCount);
        var bins = new HistogramRenderBin[renderBinCount];
        var windowGroupTotals = new int[groupCount];
        int maxBinTotal = 0;
        int windowTotal = 0;

        for (int rendered = 0; rendered < renderBinCount; rendered++)
        {
            int lo = binLo + (int)((long)rendered * windowBinCount / renderBinCount);
            int hi = binLo + (int)((long)(rendered + 1) * windowBinCount / renderBinCount);
            if (hi <= lo) { hi = lo + 1; }

            var groupCounts = new int[groupCount];
            int total = 0;

            for (int baseBin = lo; baseBin < hi; baseBin++)
            {
                int baseOffset = baseBin * slotCount;

                for (int group = 0; group < groupCount; group++)
                {
                    int sum = 0;

                    foreach (int slot in groups[group].SlotIndices) { sum += data.SlotCounts[baseOffset + slot]; }

                    groupCounts[group] += sum;
                    total += sum;
                }
            }

            long binStartTicks = baseMin + (lo * span);
            long binEndTicks = Math.Min((baseMin + (hi * span)) - 1, baseMax);

            bins[rendered] = new HistogramRenderBin(binStartTicks, binEndTicks, total, groupCounts);

            if (total > maxBinTotal) { maxBinTotal = total; }

            windowTotal += total;

            for (int group = 0; group < groupCount; group++) { windowGroupTotals[group] += groupCounts[group]; }
        }

        // Report the whole-base-bin bounds as the window so the axis, announcement, and Scope describe exactly the counted span.
        long effectiveStartTicks = baseMin + (binLo * span);
        long effectiveEndTicks = Math.Min((baseMin + ((binHi + 1) * span)) - 1, baseMax);

        FlagAnomalies(bins);

        return new HistogramRender(bins, effectiveStartTicks, effectiveEndTicks, windowTotal, maxBinTotal, windowGroupTotals);
    }

    private static void FlagAnomalies(HistogramRenderBin[] bins)
    {
        if (bins.Length < MinAnomalyBins) { return; }

        double sum = 0;
        double sumOfSquares = 0;

        foreach (HistogramRenderBin bin in bins)
        {
            sum += bin.Total;
            sumOfSquares += (double)bin.Total * bin.Total;
        }

        double mean = sum / bins.Length;
        double variance = (sumOfSquares / bins.Length) - (mean * mean);

        if (variance <= 0) { return; }

        double threshold = mean + (2 * Math.Sqrt(variance));

        for (int index = 0; index < bins.Length; index++)
        {
            if (bins[index].Total > threshold) { bins[index] = bins[index] with { IsAnomaly = true }; }
        }
    }
}

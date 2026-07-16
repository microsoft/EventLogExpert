// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public static class HistogramScale
{
    private const int StackRemainderThreshold = 64;

    public static double BarHeight(int count, int maxCount, double plotHeightPx)
    {
        if (count <= 0 || maxCount <= 0 || plotHeightPx <= 0) { return 0; }

        return Math.Max(1, Math.Sqrt(count) / Math.Sqrt(maxCount) * plotHeightPx);
    }

    public static int[] StackedGroupHeights(int[] groupCounts, int maxBinTotal, double plotHeightPx)
    {
        ArgumentNullException.ThrowIfNull(groupCounts);

        var heights = new int[groupCounts.Length];
        WriteStackedGroupHeights(groupCounts, maxBinTotal, plotHeightPx, heights);

        return heights;
    }

    public static void WriteStackedGroupHeights(int[] groupCounts, int maxBinTotal, double plotHeightPx, Span<int> heights)
    {
        ArgumentNullException.ThrowIfNull(groupCounts);

        int groupCount = groupCounts.Length;

        if (heights.Length < groupCount)
        {
            throw new ArgumentException($"Destination length {heights.Length} is smaller than the group count {groupCount}.", nameof(heights));
        }

        heights[..groupCount].Clear();
        int total = 0;
        int nonzero = 0;

        foreach (int count in groupCounts)
        {
            total += count;

            if (count > 0) { nonzero++; }
        }

        if (total <= 0 || maxBinTotal <= 0 || plotHeightPx <= 0) { return; }

        int barTotalPx = Math.Min((int)Math.Round(BarHeight(total, maxBinTotal, plotHeightPx)), (int)plotHeightPx);
        barTotalPx = Math.Max(barTotalPx, nonzero);

        Span<double> remainders = groupCount <= StackRemainderThreshold ? stackalloc double[StackRemainderThreshold] : new double[groupCount];
        remainders = remainders[..groupCount];
        int assigned = 0;

        for (int group = 0; group < groupCount; group++)
        {
            double exact = (double)barTotalPx * groupCounts[group] / total;
            heights[group] = (int)Math.Floor(exact);
            remainders[group] = exact - heights[group];
            assigned += heights[group];
        }

        for (int leftover = barTotalPx - assigned; leftover > 0; leftover--)
        {
            int best = 0;

            for (int group = 1; group < groupCount; group++)
            {
                if (remainders[group] > remainders[best]) { best = group; }
            }

            heights[best]++;
            remainders[best] = -1;
        }

        // A present group the proportional split rounded to zero steals one pixel from the tallest group so it stays visible.
        for (int group = 0; group < groupCount; group++)
        {
            if (groupCounts[group] <= 0 || heights[group] != 0) { continue; }

            int tallest = 0;

            for (int other = 1; other < groupCount; other++)
            {
                if (heights[other] > heights[tallest]) { tallest = other; }
            }

            if (heights[tallest] > 1)
            {
                heights[tallest]--;
                heights[group] = 1;
            }
        }
    }
}

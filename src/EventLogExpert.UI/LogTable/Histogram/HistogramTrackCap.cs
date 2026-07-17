// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable.Histogram;

internal static class HistogramTrackCap
{
    // Blink/WebView2 clamps a layout box's width near 33.55M px; staying comfortably under keeps the virtual scroll track representable so scrollWidth == clientWidth / WindowFraction holds and the scrollbar can still reach the final bins.
    internal const int MaxTrackWidthPx = 30_000_000;

    // The fewest window bins whose CSS scroll track (clientWidth / (windowBins / totalBins)) still fits within MaxTrackWidthPx at the given viewport width; a deeper zoom would overflow the browser's layout cap and clamp scrollWidth.
    internal static int MinBinsForWidth(int viewportWidthPx, int totalBins)
    {
        if (viewportWidthPx <= 0 || totalBins <= 0) { return 1; }

        int floorBins = (int)Math.Ceiling((double)viewportWidthPx * totalBins / MaxTrackWidthPx);

        return Math.Clamp(floorBins, 1, totalBins);
    }
}

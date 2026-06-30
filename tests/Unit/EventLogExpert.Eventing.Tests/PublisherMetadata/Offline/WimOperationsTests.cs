// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

public sealed class WimOperationsTests
{
    [Fact]
    public void TryAdvanceProgressDecile_DoesNotReReportWithinTheSameBand()
    {
        int lastReportedDecile = 0;

        Assert.True(WimOperations.TryAdvanceProgressDecile(30, ref lastReportedDecile));  // enters the 30s band.
        Assert.False(WimOperations.TryAdvanceProgressDecile(31, ref lastReportedDecile)); // same band.
        Assert.False(WimOperations.TryAdvanceProgressDecile(39, ref lastReportedDecile)); // same band.
        Assert.True(WimOperations.TryAdvanceProgressDecile(40, ref lastReportedDecile));  // next band.
    }

    [Fact]
    public void TryAdvanceProgressDecile_EmitsOncePerTenPercentBandCrossed()
    {
        int lastReportedDecile = 0; // matches ApplyImage: the 0% band is suppressed so reporting starts at 10%.
        var reported = new List<int>();

        foreach (int percent in new[] { 0, 3, 9, 10, 15, 19, 20, 25, 55, 99, 100 })
        {
            if (WimOperations.TryAdvanceProgressDecile(percent, ref lastReportedDecile)) { reported.Add(percent); }
        }

        // The first percent seen in each newly-entered 10% band reports once; 0-9 is suppressed by the initial decile 0.
        Assert.Equal([10, 20, 55, 99, 100], reported);
    }

    [Fact]
    public void TryAdvanceProgressDecile_IgnoresOutOfRangePercents()
    {
        int lastReportedDecile = -1;

        Assert.False(WimOperations.TryAdvanceProgressDecile(-1, ref lastReportedDecile));
        Assert.False(WimOperations.TryAdvanceProgressDecile(101, ref lastReportedDecile));
        Assert.Equal(-1, lastReportedDecile); // unchanged by out-of-range values.
    }
}

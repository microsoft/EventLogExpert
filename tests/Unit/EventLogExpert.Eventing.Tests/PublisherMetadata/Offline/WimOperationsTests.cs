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

        Assert.True(WimOperations.TryAdvanceProgressDecile(30, ref lastReportedDecile));
        Assert.False(WimOperations.TryAdvanceProgressDecile(31, ref lastReportedDecile));
        Assert.False(WimOperations.TryAdvanceProgressDecile(39, ref lastReportedDecile));
        Assert.True(WimOperations.TryAdvanceProgressDecile(40, ref lastReportedDecile));
    }

    [Fact]
    public void TryAdvanceProgressDecile_EmitsOncePerTenPercentBandCrossed()
    {
        int lastReportedDecile = 0;
        var reported = new List<int>();

        foreach (int percent in new[] { 0, 3, 9, 10, 15, 19, 20, 25, 55, 99, 100 })
        {
            if (WimOperations.TryAdvanceProgressDecile(percent, ref lastReportedDecile)) { reported.Add(percent); }
        }

        Assert.Equal([10, 20, 55, 99, 100], reported);
    }

    [Fact]
    public void TryAdvanceProgressDecile_IgnoresOutOfRangePercents()
    {
        int lastReportedDecile = -1;

        Assert.False(WimOperations.TryAdvanceProgressDecile(-1, ref lastReportedDecile));
        Assert.False(WimOperations.TryAdvanceProgressDecile(101, ref lastReportedDecile));
        Assert.Equal(-1, lastReportedDecile);
    }
}

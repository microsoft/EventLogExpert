// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramDataTests
{
    [Fact]
    public void GroupTotals_FoldsEveryBinThroughGroupSlotIndices()
    {
        int slotCount = LevelSeverity.SlotCount;
        int[] slots = new int[3 * slotCount];
        slots[(0 * slotCount) + (int)SeverityLevel.Error] = 2;
        slots[(1 * slotCount) + (int)SeverityLevel.Warning] = 9;
        slots[(2 * slotCount) + (int)SeverityLevel.Information] = 4;

        HistogramData data = new(
            slots,
            slotCount,
            3,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc),
            15,
            TimeSpan.FromHours(1).Ticks,
            HistogramGroups.Severity);

        int[] totals = data.GroupTotals();

        Assert.Equal([4, 9, 2], totals);
    }
}

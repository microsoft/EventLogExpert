// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Services;

public sealed class FilterCategoryItemsCacheTests : IDisposable
{
    public FilterCategoryItemsCacheTests() => FilterCategoryItemsCache.Clear();

    public void Dispose() => FilterCategoryItemsCache.Clear();

    [Fact]
    public void GetItems_LevelCategory_ReturnsAllSeverityLevelNames()
    {
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty;

        var items = FilterCategoryItemsCache.GetItems(activeLogs, FilterCategory.Level);

        Assert.Equal(Enum.GetNames<SeverityLevel>(), items);
    }

    [Fact]
    public void GetItems_LogDerivedCategory_ReturnsDistinctSortedValues()
    {
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(id: 200, source: "Bravo"),
            EventUtils.CreateTestEvent(id: 100, source: "Alpha"),
            EventUtils.CreateTestEvent(id: 100, source: "Alpha"),
            EventUtils.CreateTestEvent(id: 300, source: "Charlie")
        };

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var ids = FilterCategoryItemsCache.GetItems(activeLogs, FilterCategory.Id);
        var sources = FilterCategoryItemsCache.GetItems(activeLogs, FilterCategory.Source);

        Assert.Equal(["100", "200", "300"], ids);
        Assert.Equal(["Alpha", "Bravo", "Charlie"], sources);
    }

    [Fact]
    public void GetItems_SameSnapshotAndCategory_ReturnsCachedInstance()
    {
        var logData = new EventLogData(
            Constants.LogNameTestLog,
            PathType.LogName,
            [EventUtils.CreateTestEvent(id: 100)]);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var first = FilterCategoryItemsCache.GetItems(activeLogs, FilterCategory.Id);
        var second = FilterCategoryItemsCache.GetItems(activeLogs, FilterCategory.Id);

        Assert.True(first == second, "ImmutableArray instance should be reused for the same snapshot/category.");
    }

    [Fact]
    public void GetItems_DifferentSnapshot_RecomputesValues()
    {
        var snapshotA = ImmutableDictionary<string, EventLogData>.Empty.Add(
            Constants.LogNameTestLog,
            new EventLogData(Constants.LogNameTestLog, PathType.LogName, [EventUtils.CreateTestEvent(id: 100)]));

        var snapshotB = snapshotA.Add(
            Constants.LogNameLog2,
            new EventLogData(Constants.LogNameLog2, PathType.LogName, [EventUtils.CreateTestEvent(id: 200)]));

        var idsA = FilterCategoryItemsCache.GetItems(snapshotA, FilterCategory.Id);
        var idsB = FilterCategoryItemsCache.GetItems(snapshotB, FilterCategory.Id);

        Assert.Equal(["100"], idsA);
        Assert.Equal(["100", "200"], idsB);
    }

    [Fact]
    public void GetItems_UnsupportedCategory_ReturnsEmpty()
    {
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(
            Constants.LogNameTestLog,
            new EventLogData(Constants.LogNameTestLog, PathType.LogName, [EventUtils.CreateTestEvent()]));

        var items = FilterCategoryItemsCache.GetItems(activeLogs, FilterCategory.Description);

        Assert.Empty(items);
    }
}

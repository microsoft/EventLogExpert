// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Provider.Schema;
using EventLogExpert.ProviderDatabase.Maintenance;

namespace EventLogExpert.Provider.Database.Tests.Maintenance;

public sealed class ProviderDetailsMergerTests
{
    private const string TestDatabasePath = @"C:\test\fake.db";

    [Fact]
    public void MergeCaseInsensitiveDuplicates_CarriesSharedVersionKeyThroughMerge()
    {
        var first = EventUtils.CreateProvider("Same-Provider");
        first.VersionKey = "vk1:shared";

        var second = EventUtils.CreateProvider("same-provider");
        second.VersionKey = "vk1:shared";

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates([first, second], TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal("vk1:shared", merged[0].VersionKey);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_DeduplicatesIdenticalMessages()
    {
        var msg = EventUtils.CreateMessageModel("Same-Provider", 100, "duplicate-text", 100);

        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider", [msg]),
            EventUtils.CreateProvider("same-provider", [msg])
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Single(merged[0].Messages);
        Assert.Equal("duplicate-text", merged[0].Messages[0].Text);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_MergesDistinctMapsAcrossDuplicates()
    {
        var alpha = EventUtils.CreateProvider("Same-Provider");
        alpha.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["BusType"] = new(isBitMap: false, [new ValueMapEntry(10, "SAS")])
        };

        var beta = EventUtils.CreateProvider("same-provider");
        beta.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["State"] = new(isBitMap: false, [new ValueMapEntry(1, "Online")])
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates([alpha, beta], TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal(2, merged[0].Maps.Count);
        Assert.Equal("SAS", merged[0].Maps["BusType"].Entries[0].Name);
        Assert.Equal("Online", merged[0].Maps["State"].Entries[0].Name);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_NoDuplicates_ReturnsOriginalRows()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Provider-Alpha"),
            EventUtils.CreateProvider("Provider-Beta")
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Equal(2, merged.Count);
        Assert.Equal("Provider-Alpha", merged[0].ProviderName);
        Assert.Equal("Provider-Beta", merged[1].ProviderName);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnConflictingEventDescription_Throws()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider",
                events:
                [
                    EventUtils.CreateEventModel(100, "first", logName: "App")
                ]),
            EventUtils.CreateProvider("same-provider",
                events:
                [
                    EventUtils.CreateEventModel(100, "second", logName: "App")
                ])
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath));

        Assert.Contains("Event", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnConflictingKeywordValue_Throws()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Conflict-Provider", keywords: new Dictionary<long, string> { { 1L, "first" } }),
            EventUtils.CreateProvider("conflict-provider", keywords: new Dictionary<long, string> { { 1L, "second" } })
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath));

        Assert.Contains("Keywords", ex.Reason);
        Assert.Contains("first", ex.Reason);
        Assert.Contains("second", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnConflictingMapDefinition_Throws()
    {
        var alpha = EventUtils.CreateProvider("Same-Provider");
        alpha.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["BusType"] = new(isBitMap: false, [new ValueMapEntry(10, "SAS")])
        };

        var beta = EventUtils.CreateProvider("same-provider");
        beta.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["BusType"] = new(isBitMap: false, [new ValueMapEntry(10, "NVMe")])
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates([alpha, beta], TestDatabasePath));

        Assert.Contains("Map", ex.Reason);
        Assert.Contains("BusType", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnConflictingMessageText_Throws()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider",
                [
                    EventUtils.CreateMessageModel("Same-Provider", 1, "first", 1)
                ]),
            EventUtils.CreateProvider("same-provider",
                [
                    EventUtils.CreateMessageModel("same-provider", 1, "second", 1)
                ])
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath));

        Assert.Contains("Message", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnConflictingOpcodeValue_Throws()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Conflict-Provider", opcodes: new Dictionary<int, string> { { 5, "OpcodeA" } }),
            EventUtils.CreateProvider("conflict-provider", opcodes: new Dictionary<int, string> { { 5, "OpcodeB" } })
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath));

        Assert.Contains("Opcodes", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnConflictingTaskValue_Throws()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Conflict-Provider", tasks: new Dictionary<int, string> { { 7, "TaskA" } }),
            EventUtils.CreateProvider("conflict-provider", tasks: new Dictionary<int, string> { { 7, "TaskB" } })
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath));

        Assert.Contains("Tasks", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnDistinctVersionKeys_Throws()
    {
        var first = EventUtils.CreateProvider("Same-Provider");
        first.VersionKey = "vk1:aaa";

        var second = EventUtils.CreateProvider("same-provider");
        second.VersionKey = "vk1:bbb";

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates([first, second], TestDatabasePath));

        Assert.Contains("VersionKey", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnIdenticalKeywordKeyAndValue_DoesNotThrow()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider", keywords: new Dictionary<long, string> { { 1L, "shared" } }),
            EventUtils.CreateProvider("same-provider", keywords: new Dictionary<long, string> { { 1L, "shared" } })
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal("shared", merged[0].Keywords[1L]);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_PreservesEncounterOrder()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Z-Provider"),
            EventUtils.CreateProvider("a-Provider"),
            EventUtils.CreateProvider("M-Provider")
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Equal(["Z-Provider", "a-Provider", "M-Provider"], merged.Select(p => p.ProviderName).ToArray());
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_ResolvedFromOwningPublisher_BothEqualNonNullSucceeds()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider", resolvedFromOwningPublisher: "PublisherX"),
            EventUtils.CreateProvider("same-provider", resolvedFromOwningPublisher: "PublisherX")
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal("PublisherX", merged[0].ResolvedFromOwningPublisher);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_ResolvedFromOwningPublisher_BothNonNullDifferentThrows()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider", resolvedFromOwningPublisher: "PublisherX"),
            EventUtils.CreateProvider("same-provider", resolvedFromOwningPublisher: "PublisherY")
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath));

        Assert.Contains("ResolvedFromOwningPublisher", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_ResolvedFromOwningPublisher_BothNullProducesNull()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider", resolvedFromOwningPublisher: null),
            EventUtils.CreateProvider("same-provider", resolvedFromOwningPublisher: null)
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Null(merged[0].ResolvedFromOwningPublisher);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_ResolvedFromOwningPublisher_EitherNonNullWins()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider", resolvedFromOwningPublisher: null),
            EventUtils.CreateProvider("same-provider", resolvedFromOwningPublisher: "PublisherX")
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal("PublisherX", merged[0].ResolvedFromOwningPublisher);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_ResolvedFromOwningPublisher_TreatsEmptyAsAbsent()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider", resolvedFromOwningPublisher: ""),
            EventUtils.CreateProvider("same-provider", resolvedFromOwningPublisher: "PublisherX")
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal("PublisherX", merged[0].ResolvedFromOwningPublisher);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_TreatsEventsWithSameKeywordsInDifferentOrderAsEqual()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider",
                events:
                [
                    EventUtils.CreateEventModel(100, "desc", logName: "App", keywords: [1, 2, 3])
                ]),
            EventUtils.CreateProvider("same-provider",
                events:
                [
                    EventUtils.CreateEventModel(100, "desc", logName: "App", keywords: [3, 2, 1])
                ])
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Single(merged[0].Events);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_TreatsMessagesWithDifferentTagsAsDistinct()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider",
                [
                    EventUtils.CreateMessageModel("Same-Provider", 1, "shared", 1, tag: "TagA")
                ]),
            EventUtils.CreateProvider("same-provider",
                [
                    EventUtils.CreateMessageModel("same-provider", 1, "shared", 1, tag: "TagB")
                ])
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal(2, merged[0].Messages.Count);
        Assert.Contains(merged[0].Messages, m => m.Tag == "TagA");
        Assert.Contains(merged[0].Messages, m => m.Tag == "TagB");
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_UnionsKeywordDictionaries()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("Same-Provider", keywords: new Dictionary<long, string> { { 1L, "alpha" } }),
            EventUtils.CreateProvider("same-provider", keywords: new Dictionary<long, string> { { 2L, "beta" } })
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal(2, merged[0].Keywords.Count);
        Assert.Equal("alpha", merged[0].Keywords[1L]);
        Assert.Equal("beta", merged[0].Keywords[2L]);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_UsesFirstRowCasingAsCanonical()
    {
        var rows = new List<ProviderDetails>
        {
            EventUtils.CreateProvider("MICROSOFT-Foo"),
            EventUtils.CreateProvider("microsoft-foo"),
            EventUtils.CreateProvider("Microsoft-Foo")
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal("MICROSOFT-Foo", merged[0].ProviderName);
    }
}

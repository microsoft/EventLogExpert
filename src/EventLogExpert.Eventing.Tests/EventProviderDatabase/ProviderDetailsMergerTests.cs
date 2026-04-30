// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;

namespace EventLogExpert.Eventing.Tests.EventProviderDatabase;

public sealed class ProviderDetailsMergerTests
{
    private const string TestDatabasePath = @"C:\test\fake.db";

    [Fact]
    public void MergeCaseInsensitiveDuplicates_DeduplicatesIdenticalMessages()
    {
        var msg = new MessageModel { ProviderName = "Same-Provider", RawId = 100, ShortId = 100, Text = "duplicate-text", Tag = null };
        var rows = new List<ProviderDetails>
        {
            CreateProvider("Same-Provider", messages: [msg]),
            CreateProvider("same-provider", messages: [msg])
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Single(merged[0].Messages);
        Assert.Equal("duplicate-text", merged[0].Messages[0].Text);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_NoDuplicates_ReturnsOriginalRows()
    {
        var rows = new List<ProviderDetails>
        {
            CreateProvider("Provider-Alpha"),
            CreateProvider("Provider-Beta")
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
            CreateProvider("Same-Provider", events:
            [
                new EventModel { Id = 100, Version = 0, LogName = "App", Keywords = [], Description = "first" }
            ]),
            CreateProvider("same-provider", events:
            [
                new EventModel { Id = 100, Version = 0, LogName = "App", Keywords = [], Description = "second" }
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
            CreateProvider("Conflict-Provider", keywords: new Dictionary<long, string> { { 1L, "first" } }),
            CreateProvider("conflict-provider", keywords: new Dictionary<long, string> { { 1L, "second" } })
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath));
        Assert.Contains("Keywords", ex.Reason);
        Assert.Contains("first", ex.Reason);
        Assert.Contains("second", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnConflictingMessageText_Throws()
    {
        var rows = new List<ProviderDetails>
        {
            CreateProvider("Same-Provider", messages:
            [
                new MessageModel { ProviderName = "Same-Provider", RawId = 1, ShortId = 1, Text = "first" }
            ]),
            CreateProvider("same-provider", messages:
            [
                new MessageModel { ProviderName = "same-provider", RawId = 1, ShortId = 1, Text = "second" }
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
            CreateProvider("Conflict-Provider", opcodes: new Dictionary<int, string> { { 5, "OpcodeA" } }),
            CreateProvider("conflict-provider", opcodes: new Dictionary<int, string> { { 5, "OpcodeB" } })
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
            CreateProvider("Conflict-Provider", tasks: new Dictionary<int, string> { { 7, "TaskA" } }),
            CreateProvider("conflict-provider", tasks: new Dictionary<int, string> { { 7, "TaskB" } })
        };

        var ex = Assert.Throws<DatabaseUpgradeException>(() =>
            ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath));
        Assert.Contains("Tasks", ex.Reason);
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_OnIdenticalKeywordKeyAndValue_DoesNotThrow()
    {
        var rows = new List<ProviderDetails>
        {
            CreateProvider("Same-Provider", keywords: new Dictionary<long, string> { { 1L, "shared" } }),
            CreateProvider("same-provider", keywords: new Dictionary<long, string> { { 1L, "shared" } })
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
            CreateProvider("Z-Provider"),
            CreateProvider("a-Provider"),
            CreateProvider("M-Provider")
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Equal(["Z-Provider", "a-Provider", "M-Provider"], merged.Select(p => p.ProviderName).ToArray());
    }

    [Fact]
    public void MergeCaseInsensitiveDuplicates_ResolvedFromOwningPublisher_BothEqualNonNullSucceeds()
    {
        var rows = new List<ProviderDetails>
        {
            CreateProvider("Same-Provider", resolvedFromOwningPublisher: "PublisherX"),
            CreateProvider("same-provider", resolvedFromOwningPublisher: "PublisherX")
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
            CreateProvider("Same-Provider", resolvedFromOwningPublisher: "PublisherX"),
            CreateProvider("same-provider", resolvedFromOwningPublisher: "PublisherY")
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
            CreateProvider("Same-Provider", resolvedFromOwningPublisher: null),
            CreateProvider("same-provider", resolvedFromOwningPublisher: null)
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
            CreateProvider("Same-Provider", resolvedFromOwningPublisher: null),
            CreateProvider("same-provider", resolvedFromOwningPublisher: "PublisherX")
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
            CreateProvider("Same-Provider", resolvedFromOwningPublisher: ""),
            CreateProvider("same-provider", resolvedFromOwningPublisher: "PublisherX")
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
            CreateProvider("Same-Provider", events:
            [
                new EventModel { Id = 100, Version = 0, LogName = "App", Keywords = [1, 2, 3], Description = "desc" }
            ]),
            CreateProvider("same-provider", events:
            [
                new EventModel { Id = 100, Version = 0, LogName = "App", Keywords = [3, 2, 1], Description = "desc" }
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
            CreateProvider("Same-Provider", messages:
            [
                new MessageModel { ProviderName = "Same-Provider", RawId = 1, ShortId = 1, Text = "shared", Tag = "TagA" }
            ]),
            CreateProvider("same-provider", messages:
            [
                new MessageModel { ProviderName = "same-provider", RawId = 1, ShortId = 1, Text = "shared", Tag = "TagB" }
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
            CreateProvider("Same-Provider", keywords: new Dictionary<long, string> { { 1L, "alpha" } }),
            CreateProvider("same-provider", keywords: new Dictionary<long, string> { { 2L, "beta" } })
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
            CreateProvider("MICROSOFT-Foo"),
            CreateProvider("microsoft-foo"),
            CreateProvider("Microsoft-Foo")
        };

        var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(rows, TestDatabasePath);

        Assert.Single(merged);
        Assert.Equal("MICROSOFT-Foo", merged[0].ProviderName);
    }

    private static ProviderDetails CreateProvider(
        string name,
        IReadOnlyList<MessageModel>? messages = null,
        IReadOnlyList<EventModel>? events = null,
        IDictionary<long, string>? keywords = null,
        IDictionary<int, string>? opcodes = null,
        IDictionary<int, string>? tasks = null,
        string? resolvedFromOwningPublisher = null) =>
        new()
        {
            ProviderName = name,
            Messages = messages ?? [],
            Parameters = [],
            Events = events ?? [],
            Keywords = keywords ?? new Dictionary<long, string>(),
            Opcodes = opcodes ?? new Dictionary<int, string>(),
            Tasks = tasks ?? new Dictionary<int, string>(),
            ResolvedFromOwningPublisher = resolvedFromOwningPublisher
        };
}

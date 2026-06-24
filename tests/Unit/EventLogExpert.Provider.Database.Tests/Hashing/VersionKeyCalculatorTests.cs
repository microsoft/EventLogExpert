// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.ProviderDatabase.Hashing;

namespace EventLogExpert.Provider.Database.Tests.Hashing;

public sealed class VersionKeyCalculatorTests
{
    [Fact]
    public void Compute_DictionaryInsertionOrder_DoesNotChangeKey()
    {
        var first = EventUtils.CreateProvider("P", tasks: new Dictionary<int, string> { { 1, "A" }, { 2, "B" } });
        var second = EventUtils.CreateProvider("P", tasks: new Dictionary<int, string> { { 2, "B" }, { 1, "A" } });

        Assert.Equal(VersionKeyCalculator.Compute(first), VersionKeyCalculator.Compute(second));
    }

    [Fact]
    public void Compute_DifferentEventDescription_ChangesKey()
    {
        var first = EventUtils.CreateProvider("P", events: [EventUtils.CreateEventModel(1, description: "first")]);
        var second = EventUtils.CreateProvider("P", events: [EventUtils.CreateEventModel(1, description: "second")]);

        Assert.NotEqual(VersionKeyCalculator.Compute(first), VersionKeyCalculator.Compute(second));
    }

    [Fact]
    public void Compute_DifferentProvenance_DoesNotChangeKey()
    {
        // Provenance is source metadata, not rendering content: two byte-identical providers from different OS
        // sources must keep the SAME VersionKey so they collapse to one row on a future merge.
        var first = EventUtils.CreateProvider("P", events: [EventUtils.CreateEventModel(1, description: "same")]);
        var second = EventUtils.CreateProvider("P", events: [EventUtils.CreateEventModel(1, description: "same")]);

        first.SourceOsBuild = 19041;
        first.SourceOsRevision = 1;
        first.SourceOsEdition = "ServerStandard";
        first.SourceOsDisplayVersion = "1809";
        first.MessageFileVersion = "10.0.19041.1";

        second.SourceOsBuild = 22621;
        second.SourceOsRevision = 999;
        second.SourceOsEdition = "ServerDatacenter";
        second.SourceOsDisplayVersion = "23H2";
        second.MessageFileVersion = "10.0.22621.900";

        Assert.Equal(VersionKeyCalculator.Compute(first), VersionKeyCalculator.Compute(second));
    }

    [Fact]
    public void Compute_DifferentResolvedFromOwningPublisher_ChangesKey()
    {
        var first = EventUtils.CreateProvider("P", resolvedFromOwningPublisher: "Owner-A");
        var second = EventUtils.CreateProvider("P", resolvedFromOwningPublisher: "Owner-B");

        Assert.NotEqual(VersionKeyCalculator.Compute(first), VersionKeyCalculator.Compute(second));
    }

    [Fact]
    public void Compute_EmptyProvider_IsStableAndNonEmpty()
    {
        var first = EventUtils.CreateProvider("P");
        var second = EventUtils.CreateProvider("DifferentName");

        var firstKey = VersionKeyCalculator.Compute(first);

        Assert.StartsWith("vk1:", firstKey);
        Assert.Equal(firstKey, VersionKeyCalculator.Compute(second));
    }

    [Fact]
    public void Compute_EventKeywordOrderAndDuplicates_DoNotChangeKey()
    {
        // The merger compares keywords as a SET, so the hash must too: [1, 2, 2] and [2, 1] are the same version.
        var ordered = EventUtils.CreateProvider("P", events: [EventUtils.CreateEventModel(1, keywords: [1L, 2L, 2L])]);
        var shuffled = EventUtils.CreateProvider("P", events: [EventUtils.CreateEventModel(1, keywords: [2L, 1L])]);

        Assert.Equal(VersionKeyCalculator.Compute(ordered), VersionKeyCalculator.Compute(shuffled));
    }

    [Fact]
    public void Compute_EventListOrder_DoesNotChangeKey()
    {
        var first = EventUtils.CreateProvider("P",
            events: [EventUtils.CreateEventModel(1), EventUtils.CreateEventModel(2)]);
        var second = EventUtils.CreateProvider("P",
            events: [EventUtils.CreateEventModel(2), EventUtils.CreateEventModel(1)]);

        Assert.Equal(VersionKeyCalculator.Compute(first), VersionKeyCalculator.Compute(second));
    }

    [Fact]
    public void Compute_ExcludesTopLevelAndMessageProviderName()
    {
        // Neither the top-level provider name (the identity key) nor the per-message ProviderName (which mirrors it)
        // is content, so two recordings of the same provider under different names collapse to the same key.
        var alpha = EventUtils.CreateProvider("Alpha",
            messages: [EventUtils.CreateMessageModel("Alpha", 100, "shared text", shortId: 100)],
            events: [EventUtils.CreateEventModel(100)]);
        var beta = EventUtils.CreateProvider("Beta",
            messages: [EventUtils.CreateMessageModel("Beta", 100, "shared text", shortId: 100)],
            events: [EventUtils.CreateEventModel(100)]);

        Assert.Equal(VersionKeyCalculator.Compute(alpha), VersionKeyCalculator.Compute(beta));
    }

    [Fact]
    public void Compute_IsDeterministic_ForTheSamePayload()
    {
        var first = BuildProvider();
        var second = BuildProvider();

        Assert.Equal(VersionKeyCalculator.Compute(first), VersionKeyCalculator.Compute(second));
    }

    [Fact]
    public void Compute_IsDeterministic_WithNullStringFieldsAndMaps()
    {
        Assert.Equal(
            VersionKeyCalculator.Compute(BuildProviderWithNullsAndMaps()),
            VersionKeyCalculator.Compute(BuildProviderWithNullsAndMaps()));
    }

    [Fact]
    public void Compute_MapEntryOrder_ChangesKey()
    {
        // Bitmap decoding iterates ValueMap entries in order, so entry order is content - a reordering is a different
        // rendering and must hash differently.
        var first = EventUtils.CreateProvider("P");
        first.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["flags"] = new(isBitMap: true, [new ValueMapEntry(1, "A"), new ValueMapEntry(2, "B")])
        };

        var second = EventUtils.CreateProvider("P");
        second.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["flags"] = new(isBitMap: true, [new ValueMapEntry(2, "B"), new ValueMapEntry(1, "A")])
        };

        Assert.NotEqual(VersionKeyCalculator.Compute(first), VersionKeyCalculator.Compute(second));
    }

    [Fact]
    public void Compute_MessageProviderNameCasing_DoesNotChangeKey()
    {
        // The merger ignores MessageModel.ProviderName and the DB key is case-insensitive, so the same provider
        // recorded under different name casing (its messages tracking that casing) must still collapse.
        var upper = EventUtils.CreateProvider("Provider",
            messages: [EventUtils.CreateMessageModel("Provider", 1, "text", shortId: 1)]);
        var lower = EventUtils.CreateProvider("Provider",
            messages: [EventUtils.CreateMessageModel("provider", 1, "text", shortId: 1)]);

        Assert.Equal(VersionKeyCalculator.Compute(upper), VersionKeyCalculator.Compute(lower));
    }

    [Fact]
    public void Compute_NullVsEmptyString_ChangesKey()
    {
        var nullTemplate = EventUtils.CreateProvider("P",
            messages: [EventUtils.CreateMessageModel("P", 1, "text", template: null)]);
        var emptyTemplate = EventUtils.CreateProvider("P",
            messages: [EventUtils.CreateMessageModel("P", 1, "text", template: "")]);

        Assert.NotEqual(VersionKeyCalculator.Compute(nullTemplate), VersionKeyCalculator.Compute(emptyTemplate));
    }

    [Fact]
    public void Compute_ResolvedFromOwningPublisher_NullVsEmpty_AreTheSameKey()
    {
        // The merger treats a null and an empty owning publisher as the same "no owner", so the hash must too.
        var nullOwner = EventUtils.CreateProvider("P", resolvedFromOwningPublisher: null);
        var emptyOwner = EventUtils.CreateProvider("P", resolvedFromOwningPublisher: "");

        Assert.Equal(VersionKeyCalculator.Compute(nullOwner), VersionKeyCalculator.Compute(emptyOwner));
    }

    [Fact]
    public void Compute_StartsWithSchemePrefix()
    {
        var key = VersionKeyCalculator.Compute(BuildProvider());

        Assert.StartsWith("vk1:", key);
        Assert.True(key.Length > "vk1:".Length, "VersionKey must carry a non-empty hash body.");
    }

    [Fact]
    public void EventModelProperties_AreAllAccountedForInTheHash()
    {
        // Drift guard: adding or removing an EventModel property fails this. When it does, update EncodeEvent in
        // ProviderContentCanonicalizer AND ProviderDetailsMerger.EventsAreEquivalent in lockstep, then this set.
        var properties = typeof(EventModel).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(
            ["Description", "Id", "Keywords", "Level", "LogName", "Opcode", "Task", "Template", "Version"],
            properties);
    }

    [Fact]
    public void MessageModelProperties_AreAllAccountedForInTheHash()
    {
        // Drift guard (see EventModel test). ProviderName is intentionally NOT hashed (it mirrors the excluded
        // provider name); every other property is content the hash must cover.
        var properties = typeof(MessageModel).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(
            ["LogLink", "ProviderName", "RawId", "ShortId", "Tag", "Template", "Text"],
            properties);
    }

    private static ProviderDetails BuildProvider() =>
        EventUtils.CreateProvider("Provider",
            messages: [EventUtils.CreateMessageModel("Provider", 100, "Some message %1", shortId: 100)],
            events: [EventUtils.CreateEventModel(1000, description: "An event", keywords: [4L, 8L], template: "<template/>")],
            keywords: new Dictionary<long, string> { { 4L, "Audit" } },
            tasks: new Dictionary<int, string> { { 1, "Connect" } });

    private static ProviderDetails BuildProviderWithNullsAndMaps()
    {
        var provider = EventUtils.CreateProvider("P",
            messages: [EventUtils.CreateMessageModel("P", 1, "text", shortId: 1, logLink: null, tag: null, template: null)],
            events: [EventUtils.CreateEventModel(1, description: null, logName: null, template: null)]);

        provider.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["map"] = new(isBitMap: false, [new ValueMapEntry(1, "One")])
        };

        return provider;
    }
}

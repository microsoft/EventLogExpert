// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Provider.Tests.Resolution;

public sealed class ProviderDetailsTests
{
    [Fact]
    public void EventsSetter_InvalidatesCachedLookup()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            events: [EventUtils.CreateEventModel(100, logName: Constants.ApplicationLogName)]);

        var beforeReplace = details.GetEventsById(100);
        Assert.Single(beforeReplace);

        // Act — replace Events, which should invalidate the cached lookup
        details.Events = [EventUtils.CreateEventModel(200, logName: Constants.ApplicationLogName)];

        // Assert — old ID no longer found, new ID found
        Assert.Empty(details.GetEventsById(100));
        Assert.Single(details.GetEventsById(200));
    }

    [Fact]
    public void GetEventsById_WhenIdDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            events: [EventUtils.CreateEventModel(100, logName: Constants.ApplicationLogName)]);

        // Act
        var result = details.GetEventsById(999);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetEventsById_WhenIdExists_ReturnsMatchingEvents()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            events:
            [
                EventUtils.CreateEventModel(100, logName: Constants.ApplicationLogName),
                EventUtils.CreateEventModel(200, logName: Constants.ApplicationLogName)
            ]);

        // Act
        var result = details.GetEventsById(100);

        // Assert
        Assert.Single(result);
        Assert.Equal(100, result[0].Id);
    }

    [Fact]
    public void GetEventsById_WhenMultipleEventsShareId_ReturnsAll()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            events:
            [
                EventUtils.CreateEventModel(100, version: 0, logName: Constants.ApplicationLogName),
                EventUtils.CreateEventModel(100, version: 1, logName: Constants.ApplicationLogName)
            ]);

        // Act
        var result = details.GetEventsById(100);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(100, e.Id));
        Assert.Contains(result, e => e.Version == 0);
        Assert.Contains(result, e => e.Version == 1);
    }

    // CompactMessageStore memoizes each id's result, so a repeated lookup returns the same instance rather than
    // re-materializing - the property that lets the resolver hit the message store per event without re-parsing.
    [Fact]
    public void GetMessagesByShortId_RepeatedLookup_ReturnsCachedInstance()
    {
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [
                new MessageModel { ShortId = 7, RawId = 0x00010007, Text = "first" },
                new MessageModel { ShortId = 7, RawId = 0x00020007, Text = "second" }
            ]);

        Assert.Same(details.GetMessagesByShortId(7), details.GetMessagesByShortId(7));
    }

    [Fact]
    public void GetMessagesByShortId_WhenIdDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = 1, RawId = 1, Text = "x" }]);

        // Act
        var result = details.GetMessagesByShortId(999);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetMessagesByShortId_WhenMessageHasRareFields_PreservesThemByteIdentical()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = 9, RawId = 9, Text = "rare", LogLink = "System", Tag = "t", Template = "<tmpl/>" }]);

        // Act
        var message = Assert.Single(details.GetMessagesByShortId(9));

        // Assert
        Assert.Equal("rare", message.Text);
        Assert.Equal("System", message.LogLink);
        Assert.Equal("t", message.Tag);
        Assert.Equal("<tmpl/>", message.Template);
        Assert.Equal(9, message.RawId);
    }

    [Fact]
    public void GetMessagesByShortId_WhenMultipleShareShortId_PreservesOriginalOrder()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [
                new MessageModel { ShortId = 7, RawId = 0x00010007, Text = "first" },
                new MessageModel { ShortId = 7, RawId = 0x00020007, Text = "second" },
                new MessageModel { ShortId = 7, RawId = 0x00030007, Text = "third" }
            ]);

        // Act
        var result = details.GetMessagesByShortId(7);

        // Assert
        Assert.Equal(["first", "second", "third"], result.Select(m => m.Text));
        Assert.Equal([0x00010007, 0x00020007, 0x00030007], result.Select(m => m.RawId));
    }

    [Fact]
    public void GetMessagesByShortId_WhenNegativeShortId_MatchesViaUnsignedPromotion()
    {
        // Arrange — ShortId is signed short; (ushort)(-1) == 65535
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = -1, RawId = 1, Text = "negative" }]);

        // Act — callers use ushort-to-int promotion: (int)(ushort)(-1) == 65535
        var result = details.GetMessagesByShortId(65535);

        // Assert
        Assert.Single(result);
        Assert.Equal("negative", result[0].Text);
    }

    [Fact]
    public void GetMessagesByShortId_WhenPositiveShortIdExists_ReturnsMatchingMessage()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = 5, RawId = 5, Text = "positive" }]);

        // Act
        var result = details.GetMessagesByShortId(5);

        // Assert
        Assert.Single(result);
        Assert.Equal("positive", result[0].Text);
    }

    [Fact]
    public void GetParameterByRawId_RepeatedLookup_ReturnsCachedInstance()
    {
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);
        details.Parameters = [new MessageModel { RawId = 42, Text = "only" }];

        Assert.Same(details.GetParameterByRawId(42), details.GetParameterByRawId(42));
    }

    [Fact]
    public void GetParameterByRawId_WhenDuplicateRawIds_ReturnsFirstWinningMatch()
    {
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);
        details.Parameters =
        [
            new MessageModel { RawId = 42, Text = "first", Tag = "A" },
            new MessageModel { RawId = 42, Text = "second", Tag = "B" }
        ];

        var match = details.GetParameterByRawId(42);

        Assert.NotNull(match);
        Assert.Equal("first", match.Text);
        Assert.Equal("A", match.Tag);
    }

    [Fact]
    public void GetParameterByRawId_WhenRawIdDoesNotExist_ReturnsNull()
    {
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);
        details.Parameters = [new MessageModel { RawId = 1, Text = "only" }];

        Assert.Null(details.GetParameterByRawId(999));
    }

    [Fact]
    public void IsEmpty_WhenAllCollectionsEmptyAndNoFallback_ReturnsTrue()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);

        // Act + Assert
        Assert.True(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenEventsPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            events: [EventUtils.CreateEventModel(1, logName: Constants.ApplicationLogName)]);

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenKeywordsPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            keywords: new Dictionary<long, string> { { 1, "kw" } });

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenMessagesPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            [new MessageModel { ShortId = 1, RawId = 1, Text = "x" }]);

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenOpcodesPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            opcodes: new Dictionary<int, string> { { 1, "op" } });

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenParametersPopulated_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = Constants.TestProviderLongName,
            Parameters = [new MessageModel { ShortId = 1, RawId = 1, Text = "p" }]
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenResolvedFromOwningPublisherSet_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider("Channel/Operational",
            resolvedFromOwningPublisher: "Microsoft-Windows-OwningPublisher");

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenTasksPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            tasks: new Dictionary<int, string> { { 1, "task" } });

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void Messages_ViewEnumeration_PreservesEveryFieldForCommonAndRareEntries()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [
                new MessageModel { ShortId = 1, RawId = 1, Text = "common", ProviderName = Constants.TestProviderLongName },
                new MessageModel { ShortId = 2, RawId = 2, Text = "rare", LogLink = "Application", Tag = "x", ProviderName = Constants.TestProviderLongName }
            ]);

        // Act
        var all = details.Messages.ToList();

        // Assert
        Assert.Equal(2, details.Messages.Count);
        Assert.Equal("common", all[0].Text);
        Assert.Null(all[0].LogLink);
        Assert.Equal(Constants.TestProviderLongName, all[0].ProviderName);
        Assert.Equal("rare", all[1].Text);
        Assert.Equal("Application", all[1].LogLink);
        Assert.Equal("x", all[1].Tag);
    }

    [Fact]
    public void MessagesSetter_InvalidatesCachedLookup()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = 1, RawId = 1, Text = "original" }]);

        var beforeReplace = details.GetMessagesByShortId(1);
        Assert.Single(beforeReplace);

        // Act — replace Messages, which should invalidate the cached lookup
        details.Messages = [new MessageModel { ShortId = 2, RawId = 2, Text = "replaced" }];

        // Assert — old ShortId no longer found, new ShortId found
        Assert.Empty(details.GetMessagesByShortId(1));
        Assert.Single(details.GetMessagesByShortId(2));
    }

    [Fact]
    public void ParametersSetter_InvalidatesCachedLookup()
    {
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);
        details.Parameters = [new MessageModel { RawId = 1, Text = "original" }];

        var beforeReplace = details.GetParameterByRawId(1);
        Assert.NotNull(beforeReplace);
        Assert.Equal("original", beforeReplace.Text);

        details.Parameters = [new MessageModel { RawId = 2, Text = "replaced" }];

        Assert.Null(details.GetParameterByRawId(1));

        var replaced = details.GetParameterByRawId(2);
        Assert.NotNull(replaced);
        Assert.Equal("replaced", replaced.Text);
    }

    // A lazy source is consulted per id and its view is materialized on demand; setting it and doing point lookups must
    // never force a full MaterializeAll (which would defeat the lazy message loading the source exists to provide).
    [Fact]
    public void SetLazyMessageSource_LooksUpPerIdWithoutMaterializingEverything()
    {
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);
        var source = new CountingLazySource(
            byShortId: new Dictionary<int, IReadOnlyList<MessageModel>>
            {
                [7] = [new MessageModel { ShortId = 7, RawId = 7, Text = "a" }]
            },
            byRawId: new Dictionary<long, MessageModel>());

        details.SetLazyMessageSource(source);
        _ = details.GetMessagesByShortId(7);
        _ = details.GetMessagesByShortId(8);

        Assert.Equal(0, source.MaterializeAllCalls);
    }

    // SetLazyMessageSource swaps in an external lazy source (used by the offline/WEVT readers) so message lookups and
    // the Messages view route through it instead of a built-in CompactMessageStore.
    [Fact]
    public void SetLazyMessageSource_RoutesMessageLookupsAndViewToTheSource()
    {
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);
        var message = new MessageModel { ShortId = 7, RawId = 7, Text = "lazy", ProviderName = Constants.TestProviderLongName };
        var source = new CountingLazySource(
            byShortId: new Dictionary<int, IReadOnlyList<MessageModel>> { [7] = [message] },
            byRawId: new Dictionary<long, MessageModel>());

        details.SetLazyMessageSource(source);

        Assert.Same(source, details.MessageSource);
        Assert.Equal([message], details.GetMessagesByShortId(7));
        Assert.Equal(1, source.ShortIdCalls);
        Assert.Equal(["lazy"], details.Messages.Select(m => m.Text));
    }

    [Fact]
    public void SetLazyParameterSource_RoutesParameterLookupsToTheSource()
    {
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);
        var parameter = new MessageModel { RawId = 42, Text = "lazy-param", ProviderName = Constants.TestProviderLongName };
        var source = new CountingLazySource(
            byShortId: new Dictionary<int, IReadOnlyList<MessageModel>>(),
            byRawId: new Dictionary<long, MessageModel> { [42] = parameter });

        details.SetLazyParameterSource(source);

        Assert.Same(source, details.ParameterSource);
        Assert.Same(parameter, details.GetParameterByRawId(42));
        Assert.Null(details.GetParameterByRawId(999));
        Assert.Equal(2, source.RawIdCalls);
    }

    private sealed class CountingLazySource(
        IReadOnlyDictionary<int, IReadOnlyList<MessageModel>> byShortId,
        IReadOnlyDictionary<long, MessageModel> byRawId) : ILazyMessageSource
    {
        public int Count => byShortId.Values.Sum(list => list.Count);

        public int MaterializeAllCalls { get; private set; }

        public int RawIdCalls { get; private set; }

        public int ShortIdCalls { get; private set; }

        public IReadOnlyList<MessageModel> AsView() => [.. byShortId.Values.SelectMany(list => list)];

        public MessageModel? GetByRawIdFirst(long rawId)
        {
            RawIdCalls++;

            return byRawId.GetValueOrDefault(rawId);
        }

        public IReadOnlyList<MessageModel> GetByShortId(int shortId)
        {
            ShortIdCalls++;

            return byShortId.TryGetValue(shortId, out var list) ? list : [];
        }

        public IReadOnlyList<MessageModel> MaterializeAll()
        {
            MaterializeAllCalls++;

            return AsView();
        }
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventColumnStoreReaderTests
{
    private const long ContentVersion = 42;
    private const int Generation = 3;

    private static readonly EventLogId s_logId = EventLogId.Create();
    private static readonly DateTime s_time = new(2021, 6, 15, 10, 20, 30, DateTimeKind.Utc);

    [Fact]
    public void GetField_ForeignLogIdLocator_ThrowsArgumentException()
    {
        IEventColumnReader reader = SealedReader();
        EventLocator foreign = new(EventLogId.Create(), reader.Generation, 0);

        Assert.Throws<ArgumentException>(() => reader.GetField(foreign, EventFieldId.Id));
    }

    [Fact]
    public void GetField_StaleGenerationLocator_ThrowsArgumentException()
    {
        IEventColumnReader reader = SealedReader();
        EventLocator stale = new(reader.LogId, reader.Generation + 1, 0);

        Assert.Throws<ArgumentException>(() => reader.GetField(stale, EventFieldId.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetKeywords_ReturnsEachRowsKeywordSequence(bool sealRows)
    {
        ResolvedEvent[] sample =
        [
            new ResolvedEvent("Security", LogPathType.Channel)
            {
                Id = 1, TimeCreated = s_time, Keywords = ["Audit Success", "Classic"]
            },
            new ResolvedEvent("Application", LogPathType.Channel) { Id = 2, TimeCreated = s_time },
            new ResolvedEvent("System", LogPathType.Channel) { Id = 3, TimeCreated = s_time, Keywords = ["Classic"] }
        ];

        IEventColumnReader reader = ReaderOver(sample, sealRows);

        Assert.Equal(["Audit Success", "Classic"], reader.GetKeywords(reader.LocatorAt(0)));
        Assert.Empty(reader.GetKeywords(reader.LocatorAt(1)));
        Assert.Equal(["Classic"], reader.GetKeywords(reader.LocatorAt(2)));
    }

    private static IEventColumnReader ReaderOver(ResolvedEvent[] sample, bool sealRows) =>
        (sealRows
            ? EventColumnStore.Build(sample, Generation, ContentVersion)
            : EventColumnStore.Build([], Generation, ContentVersion).Append(sample))
        .CreateReader(s_logId);

    private static IEventColumnReader SealedReader() =>
        ReaderOver([new ResolvedEvent("Security", LogPathType.Channel) { Id = 1, TimeCreated = s_time }], sealRows: true);
}

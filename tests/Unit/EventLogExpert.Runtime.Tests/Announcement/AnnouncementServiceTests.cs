// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Announcement;
using NSubstitute;
using AnnouncementPayload = EventLogExpert.Runtime.Announcement.Announcement;

namespace EventLogExpert.Runtime.Tests.Announcement;

public sealed class AnnouncementServiceTests
{
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    [Fact]
    public void Announce_CurrentAnnouncement_ReflectsLastMessage()
    {
        var svc = new AnnouncementService(_traceLogger);
        svc.Announce("Settings saved");

        Assert.Equal(new AnnouncementPayload.Text("Settings saved"), svc.Current.Payload);
    }

    [Fact]
    public void Announce_DifferentMessages_BothReflectedInOrder()
    {
        var svc = new AnnouncementService(_traceLogger);
        var states = new List<AnnouncementPayload>();
        svc.StateChanged += () => states.Add(svc.Current.Payload);

        svc.Announce("Settings saved");
        svc.Announce("Database imported");

        Assert.Equal(2, states.Count);
        Assert.Equal(new AnnouncementPayload.Text("Settings saved"), states[0]);
        Assert.Equal(new AnnouncementPayload.Text("Database imported"), states[1]);
    }

    [Fact]
    public void Announce_NullMessage_Throws()
    {
        var svc = new AnnouncementService(_traceLogger);
        Assert.Throws<ArgumentNullException>(() => svc.Announce(null!));
    }

    [Fact]
    public void Announce_RaisesStateChanged()
    {
        var svc = new AnnouncementService(_traceLogger);
        int callCount = 0;
        svc.StateChanged += () => callCount++;

        svc.Announce("Settings saved");

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Announce_SubscriberThrows_LaterSubscribersStillInvoked_AndExceptionLogged()
    {
        // Mirrors BannerService.RaiseSafely contract: per-subscriber fault isolation prevents
        // one throwing handler from blocking later ones or surfacing to the Announce caller.
        var svc = new AnnouncementService(_traceLogger);
        int secondCallCount = 0;
        svc.StateChanged += static () => throw new InvalidOperationException("boom");
        svc.StateChanged += () => secondCallCount++;

        svc.Announce("Settings saved");

        Assert.Equal(1, secondCallCount);
        _traceLogger.Received(1).Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public void Announce_TwoIdenticalMessages_SequenceIncrements_ForReannouncement()
    {
        // The service no longer bakes the re-announce \u200B toggle into the payload (that moved to AnnouncerHost).
        // It instead advances a monotonic sequence so the host can mutate the rendered DOM text even when two
        // consecutive announcements carry identical content. The payloads match; the sequence must differ.
        var svc = new AnnouncementService(_traceLogger);
        svc.Announce("Database imported");
        var first = svc.Current;

        svc.Announce("Database imported");
        var second = svc.Current;

        Assert.Equal(first.Payload, second.Payload);
        Assert.NotEqual(first.Sequence, second.Sequence);
    }

    [Fact]
    public void Ctor_NullTraceLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AnnouncementService(null!));
    }
}

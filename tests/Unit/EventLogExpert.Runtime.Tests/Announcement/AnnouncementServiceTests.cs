// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Announcement;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Announcement;

public sealed class AnnouncementServiceTests
{
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    [Fact]
    public void Announce_CurrentAnnouncement_ReflectsLastMessage()
    {
        var svc = new AnnouncementService(_traceLogger);
        svc.Announce("Settings saved");

        Assert.StartsWith("Settings saved", svc.CurrentAnnouncement);
    }

    [Fact]
    public void Announce_DifferentMessages_BothReflectedInOrder()
    {
        var svc = new AnnouncementService(_traceLogger);
        var states = new List<string>();
        svc.StateChanged += () => states.Add(svc.CurrentAnnouncement);

        svc.Announce("Settings saved");
        svc.Announce("Database imported");

        Assert.Equal(2, states.Count);
        Assert.StartsWith("Settings saved", states[0]);
        Assert.StartsWith("Database imported", states[1]);
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
    public void Announce_TwoIdenticalMessages_DomTextMutatesForReannouncement()
    {
        // SR live regions do not re-announce if the text node does not change. The seq-toggle
        // appends \u200B (zero-width space) on odd-seq announcements so the DOM string differs even
        // when the visible content matches the prior announcement. NVDA/JAWS/VoiceOver do not
        // pronounce ZWS, so the difference is invisible to users.
        var svc = new AnnouncementService(_traceLogger);
        svc.Announce("Database imported");
        var first = svc.CurrentAnnouncement;

        svc.Announce("Database imported");
        var second = svc.CurrentAnnouncement;

        Assert.NotEqual(first, second);
        Assert.StartsWith("Database imported", first);
        Assert.StartsWith("Database imported", second);
    }

    [Fact]
    public void Ctor_NullTraceLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AnnouncementService(null!));
    }
}

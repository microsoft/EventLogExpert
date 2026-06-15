// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Scenarios;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ChannelPresenceProbeTests
{
    [Fact]
    public void FailedRead_DoesNotCacheEmpty_AndRetries()
    {
        var calls = 0;

        var probe = new ChannelPresenceProbe(Logger(), () =>
        {
            calls++;

            return calls == 1
                ? throw new InvalidOperationException("event log service unavailable")
                : ["System"];
        });

        Assert.Empty(probe.GetPresentChannels());

        Assert.Contains("System", probe.GetPresentChannels());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void GetPresentChannels_ReadsOnceThenCaches()
    {
        var calls = 0;

        var probe = new ChannelPresenceProbe(Logger(), () =>
        {
            calls++;

            return ["System"];
        });

        _ = probe.GetPresentChannels();
        _ = probe.GetPresentChannels();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void IsPresent_IsCaseInsensitive()
    {
        var probe = new ChannelPresenceProbe(Logger(), static () => ["System", "Security"]);

        Assert.True(probe.IsPresent("system"));
        Assert.True(probe.IsPresent("SECURITY"));
        Assert.False(probe.IsPresent("Application"));
    }

    private static ITraceLogger Logger() => Substitute.For<ITraceLogger>();
}

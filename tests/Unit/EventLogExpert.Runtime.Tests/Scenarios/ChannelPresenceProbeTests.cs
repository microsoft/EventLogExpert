// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Scenarios;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ChannelPresenceProbeTests
{
    [Fact]
    public void FailedRead_DoesNotCacheEmpty_AndRetries()
    {
        var calls = 0;

        var probe = new ChannelPresenceProbe(Logger(), EnablementReader(), [], () =>
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
    public void GetPresentChannels_DoesNotEnrichChannelConfig()
    {
        var config = ConfigReader(new Dictionary<string, ChannelConfig>
        {
            ["System"] = new(true, ChannelAccess.Accessible, EvtChannelType.Admin)
        });
        var probe = new ChannelPresenceProbe(
            Logger(),
            config,
            ["System"],
            static () => ["System"]);

        _ = probe.GetPresentChannels();

        config.DidNotReceive().ReadConfig(Arg.Any<string>());
    }

    [Fact]
    public void GetPresentChannels_ReadsOnceThenCaches()
    {
        var calls = 0;

        var probe = new ChannelPresenceProbe(Logger(), EnablementReader(), [], () =>
        {
            calls++;

            return ["System"];
        });

        _ = probe.GetPresentChannels();
        _ = probe.GetPresentChannels();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetReadinessAsync_EnrichesUnprobedChannelsLazily()
    {
        var config = ConfigReader(new Dictionary<string, ChannelConfig>
        {
            ["Security"] = new(false, ChannelAccess.RequiresElevation, EvtChannelType.Admin)
        });
        var probe = new ChannelPresenceProbe(
            Logger(),
            config,
            [],
            static () => ["Security"]);

        var readiness = await probe.GetReadinessAsync(["Security"], TestContext.Current.CancellationToken);

        Assert.Equal(ChannelEnablement.Disabled, readiness.Single().Enablement);
        Assert.Equal(ChannelAccess.RequiresElevation, readiness.Single().Access);
    }

    [Fact]
    public async Task GetReadinessAsync_ParameterlessDoesNotEnrichNonCatalogChannels()
    {
        var config = ConfigReader(new Dictionary<string, ChannelConfig>
        {
            ["System"] = new(true, ChannelAccess.Accessible, EvtChannelType.Admin),
            ["Custom"] = new(false, ChannelAccess.RequiresElevation, EvtChannelType.Admin)
        });
        var probe = new ChannelPresenceProbe(
            Logger(),
            config,
            ["System"],
            static () => ["System", "Custom"]);

        var allReadiness = await probe.GetReadinessAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)
            {
                Access = ChannelAccess.Accessible
            },
            allReadiness);
        Assert.Contains(new ChannelReadiness("Custom", ChannelPresence.Present, ChannelEnablement.Unknown), allReadiness);
        config.Received(1).ReadConfig("System");
        config.DidNotReceive().ReadConfig("Custom");

        var customReadiness = await probe.GetReadinessAsync(["Custom"], TestContext.Current.CancellationToken);

        Assert.Equal(ChannelEnablement.Disabled, customReadiness.Single().Enablement);
        Assert.Equal(ChannelAccess.RequiresElevation, customReadiness.Single().Access);
        config.Received(1).ReadConfig("Custom");
    }

    [Fact]
    public async Task GetReadinessAsync_ReportsPresentAbsentAndEnablement()
    {
        var config = ConfigReader(new Dictionary<string, ChannelConfig>
        {
            ["System"] = new(true, ChannelAccess.Accessible, EvtChannelType.Admin),
            ["Application"] = new(false, ChannelAccess.RequiresElevation, EvtChannelType.Admin)
        });

        var probe = new ChannelPresenceProbe(
            Logger(),
            config,
            ["System"],
            static () => ["System"]);

        var readiness = await probe.GetReadinessAsync(
            ["System", "Application", "Security"],
            TestContext.Current.CancellationToken);

        Assert.Contains(
            new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)
            {
                Access = ChannelAccess.Accessible
            },
            readiness);
        Assert.Contains(
            new ChannelReadiness("Application", ChannelPresence.Absent, ChannelEnablement.Disabled)
            {
                Access = ChannelAccess.RequiresElevation
            },
            readiness);
        Assert.Contains(new ChannelReadiness("Security", ChannelPresence.Absent, ChannelEnablement.Unknown), readiness);
    }

    [Fact]
    public async Task GetReadinessAsync_WhenEnumerationFails_ReturnsUnknownPresence()
    {
        var probe = new ChannelPresenceProbe(
            Logger(),
            EnablementReader(),
            [],
            static () => throw new InvalidOperationException("event log service unavailable"));

        var readiness = await probe.GetReadinessAsync(["System"], TestContext.Current.CancellationToken);

        Assert.Equal(new ChannelReadiness("System", ChannelPresence.Unknown, ChannelEnablement.Unknown), readiness.Single());
    }

    [Fact]
    public async Task GetReadinessAsync_WhenOneChannelConfigThrows_StillEnrichesOtherChannels()
    {
        var config = Substitute.For<IChannelConfigReader>();
        config.ReadConfig("Faulting").Returns(_ => throw new InvalidOperationException("channel config read failed"));
        config.ReadConfig("Healthy").Returns(new ChannelConfig(true, ChannelAccess.Accessible, EvtChannelType.Admin));

        var probe = new ChannelPresenceProbe(Logger(), config, [], static () => ["Faulting", "Healthy"]);

        var readiness = await probe.GetReadinessAsync(["Faulting", "Healthy"], TestContext.Current.CancellationToken);

        var faulting = readiness.Single(item => item.Channel == "Faulting");
        var healthy = readiness.Single(item => item.Channel == "Healthy");

        Assert.Equal(ChannelEnablement.Unknown, faulting.Enablement);
        Assert.Equal(ChannelAccess.Unknown, faulting.Access);
        Assert.Equal(ChannelEnablement.Enabled, healthy.Enablement);
        Assert.Equal(ChannelAccess.Accessible, healthy.Access);
    }

    [Fact]
    public async Task Invalidate_ClearsCachedSnapshot()
    {
        var calls = 0;
        var probe = new ChannelPresenceProbe(Logger(), EnablementReader(), [], () =>
        {
            calls++;
            return calls == 1 ? ["System"] : ["Application"];
        });

        Assert.Contains("System", await Channels(probe));

        probe.Invalidate();

        Assert.Contains("Application", await Channels(probe));
    }

    [Fact]
    public void IsPresent_IsCaseInsensitive()
    {
        var probe = new ChannelPresenceProbe(Logger(), EnablementReader(), [], static () => ["System", "Security"]);

        Assert.True(probe.IsPresent("system"));
        Assert.True(probe.IsPresent("SECURITY"));
        Assert.False(probe.IsPresent("Application"));
    }

    [Fact]
    public void TryGetPresentChannels_WhenReadFails_ReturnsNull()
    {
        var probe = new ChannelPresenceProbe(
            Logger(),
            EnablementReader(),
            [],
            static () => throw new InvalidOperationException("event log service unavailable"));

        Assert.Null(probe.TryGetPresentChannels());
    }

    [Fact]
    public void TryGetPresentChannels_WhenReadSucceeds_ReturnsChannels()
    {
        var probe = new ChannelPresenceProbe(Logger(), EnablementReader(), [], static () => ["System", "Security"]);

        var channels = probe.TryGetPresentChannels();

        Assert.NotNull(channels);
        Assert.Contains("System", channels);
    }

    private static async Task<ImmutableArray<string>> Channels(IChannelReadinessService service) =>
        [.. (await service.GetReadinessAsync(TestContext.Current.CancellationToken)).Select(readiness => readiness.Channel)];

    private static IChannelConfigReader ConfigReader(IReadOnlyDictionary<string, ChannelConfig>? values = null)
    {
        var reader = Substitute.For<IChannelConfigReader>();
        reader.ReadConfig(Arg.Any<string>())
            .Returns(call =>
            {
                var channel = call.Arg<string>()!;
                return values is not null && values.TryGetValue(channel, out var config)
                    ? config
                    : ChannelConfig.Unknown;
            });

        return reader;
    }

    private static IChannelConfigReader EnablementReader() => ConfigReader();

    private static ITraceLogger Logger() => Substitute.For<ITraceLogger>();
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Scenarios.Catalog;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ScenarioQueryServiceTests
{
    [Fact]
    public void GetInAppScenarios_ExcludesScenario_WhenNoChannelLoaded()
    {
        var registry = ScenarioTestData.Registry(ScenarioTestData.Single("system", "System", 1000));

        var service = new ScenarioQueryService(registry, ReadinessService([]));

        Assert.Empty(service.GetInAppScenarios(["Application"]));
    }

    [Fact]
    public void GetInAppScenarios_MatchesLoadedLogName_CaseInsensitive()
    {
        var registry = ScenarioTestData.Registry(
            ScenarioTestData.Single("system", "System", 1000),
            ScenarioTestData.Single("application", "Application", 1001));

        var service = new ScenarioQueryService(registry, ReadinessService([]));

        Assert.Equal(["system"], service.GetInAppScenarios(["system"]).Select(scenario => scenario.Id));
    }

    [Fact]
    public void GetInAppScenarios_ShowsCombinedScenario_WhenAnyChannelLoaded()
    {
        var registry = ScenarioTestData.Registry(ScenarioTestData.Combined("combined", "System", "Security"));

        var service = new ScenarioQueryService(registry, ReadinessService([]));

        Assert.Single(service.GetInAppScenarios(["Security"]));
    }

    [Fact]
    public async Task GetLivePresenceAsync_WhenChannelsReadable_IsKnownWithChannels()
    {
        var service = new ScenarioQueryService(
            ScenarioTestData.Registry(ScenarioTestData.Single("system", "System", 1000)),
            ReadinessService([new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)]));

        var presence = await service.GetLivePresenceAsync();

        Assert.True(presence.Known);
        Assert.Contains("System", presence.Present);
    }

    [Fact]
    public async Task GetLivePresenceAsync_WhenChannelsUnreadable_IsUnknown()
    {
        var service = new ScenarioQueryService(
            ScenarioTestData.Registry(ScenarioTestData.Single("system", "System", 1000)),
            ReadinessService([new ChannelReadiness("System", ChannelPresence.Unknown, ChannelEnablement.Unknown)]));

        var presence = await service.GetLivePresenceAsync();

        Assert.False(presence.Known);
        Assert.Empty(presence.Present);
    }

    [Fact]
    public void GetSplashScenarios_ExcludesNonChannelPresenceGating()
    {
        var registry = ScenarioTestData.Registry(
            ScenarioTestData.Single("channel-gated", "System", 1000),
            ScenarioTestData.Single("source-gated", "Application", 1001) with { Gating = ScenarioGating.SourceRegistration });

        var service = new ScenarioQueryService(registry, ReadinessService([]));

        Assert.Equal(["channel-gated"], service.GetSplashScenarios().Select(scenario => scenario.Id));
    }

    [Fact]
    public void GetSplashScenarios_ReturnsAllChannelPresenceScenarios_RegardlessOfLocalAvailability()
    {
        var registry = ScenarioTestData.Registry(
            ScenarioTestData.Single("present", "System", 1000),
            ScenarioTestData.Single("absent", "Microsoft-Windows-DNS-Client/Operational", 1014));
        var service = new ScenarioQueryService(
            registry,
            ReadinessService([new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)]));

        var ids = service.GetSplashScenarios().Select(scenario => scenario.Id).ToHashSet();

        Assert.Contains("present", ids);
        Assert.Contains("absent", ids);
        Assert.Equal(2, ids.Count);
    }

    private static IChannelReadinessService ReadinessService(ImmutableArray<ChannelReadiness> readiness)
    {
        var service = Substitute.For<IChannelReadinessService>();
        service.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(readiness);

        return service;
    }
}

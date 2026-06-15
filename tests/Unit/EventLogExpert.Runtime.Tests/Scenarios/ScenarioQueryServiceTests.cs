// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Scenarios;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ScenarioQueryServiceTests
{
    [Fact]
    public void GetInAppScenarios_ExcludesScenario_WhenNoChannelLoaded()
    {
        var registry = ScenarioTestData.Registry(ScenarioTestData.Single("system", "System", 1000));

        var service = new ScenarioQueryService(registry, Substitute.For<IChannelPresenceProbe>());

        Assert.Empty(service.GetInAppScenarios(["Application"]));
    }

    [Fact]
    public void GetInAppScenarios_MatchesLoadedLogName_CaseInsensitive()
    {
        var registry = ScenarioTestData.Registry(
            ScenarioTestData.Single("system", "System", 1000),
            ScenarioTestData.Single("application", "Application", 1001));

        var service = new ScenarioQueryService(registry, Substitute.For<IChannelPresenceProbe>());

        Assert.Equal(["system"], service.GetInAppScenarios(["system"]).Select(scenario => scenario.Id));
    }

    [Fact]
    public void GetInAppScenarios_ShowsCombinedScenario_WhenAnyChannelLoaded()
    {
        var registry = ScenarioTestData.Registry(ScenarioTestData.Combined("combined", "System", "Security"));

        var service = new ScenarioQueryService(registry, Substitute.For<IChannelPresenceProbe>());

        Assert.Single(service.GetInAppScenarios(["Security"]));
    }

    [Fact]
    public void GetSplashScenarios_HidesScenariosWhoseChannelsAreAbsent()
    {
        var probe = Substitute.For<IChannelPresenceProbe>();
        probe.GetPresentChannels().Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System" });

        var registry = ScenarioTestData.Registry(
            ScenarioTestData.Single("present", "System", 1000),
            ScenarioTestData.Single("absent", "Microsoft-Windows-DNS-Client/Operational", 1014));

        var service = new ScenarioQueryService(registry, probe);

        Assert.Equal(["present"], service.GetSplashScenarios().Select(scenario => scenario.Id));
    }
}

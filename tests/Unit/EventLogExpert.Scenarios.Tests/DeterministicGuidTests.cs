// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Common;

namespace EventLogExpert.Scenarios.Tests;

public sealed class DeterministicGuidTests
{
    [Fact]
    public void Create_DiffersByName()
    {
        var first = DeterministicGuid.Create(DeterministicGuid.ScenarioNamespace, "scenario-a");
        var second = DeterministicGuid.Create(DeterministicGuid.ScenarioNamespace, "scenario-b");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Create_IsDeterministic()
    {
        var first = DeterministicGuid.Create(DeterministicGuid.ScenarioNamespace, "newly-installed-services");
        var second = DeterministicGuid.Create(DeterministicGuid.ScenarioNamespace, "newly-installed-services");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_MatchesPublishedRfc4122Version5Vector()
    {
        // Published RFC 4122 v5 vector: DNS namespace + www.example.com.
        var dnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        var result = DeterministicGuid.Create(dnsNamespace, "www.example.com");

        Assert.Equal(new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"), result);
    }

    [Fact]
    public void Create_ProducesVersion5Variant10()
    {
        var result = DeterministicGuid.Create(DeterministicGuid.ScenarioNamespace, "anything");
        var bytes = result.ToByteArray();

        // .NET layout: version in byte[7] high nibble, variant in byte[8].
        Assert.Equal(0x50, bytes[7] & 0xF0);
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }
}

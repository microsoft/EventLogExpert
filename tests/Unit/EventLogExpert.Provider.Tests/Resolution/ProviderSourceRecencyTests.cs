// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Provider.Tests.Resolution;

public sealed class ProviderSourceRecencyTests
{
    [Fact]
    public void Compare_PrefersNewerOsBuild()
    {
        var older = WithProvenance(build: 17763);
        var newer = WithProvenance(build: 20348);

        Assert.True(ProviderSourceRecency.Compare(newer, older) > 0);
        Assert.True(ProviderSourceRecency.Compare(older, newer) < 0);
    }

    [Fact]
    public void Compare_PresentProvenanceOutranksAbsent()
    {
        var present = WithProvenance(build: 17763);
        var absent = WithProvenance();

        Assert.True(ProviderSourceRecency.Compare(present, absent) > 0);
        Assert.True(ProviderSourceRecency.Compare(absent, present) < 0);
    }

    [Fact]
    public void Compare_WhenBothAbsent_ReturnsZero() =>
        Assert.Equal(0, ProviderSourceRecency.Compare(WithProvenance(), WithProvenance()));

    [Fact]
    public void Compare_WhenBuildTies_PrefersNewerRevisionThenFileVersion()
    {
        var older = WithProvenance(build: 20348, revision: 100, messageFileVersion: "10.0.20348.100");
        var newerRevision = WithProvenance(build: 20348, revision: 2000, messageFileVersion: "10.0.20348.100");

        Assert.True(ProviderSourceRecency.Compare(newerRevision, older) > 0);

        var olderFile = WithProvenance(build: 20348, revision: 2000, messageFileVersion: "10.0.20348.100");
        var newerFile = WithProvenance(build: 20348, revision: 2000, messageFileVersion: "10.0.20348.900");

        Assert.True(ProviderSourceRecency.Compare(newerFile, olderFile) > 0);
    }

    [Fact]
    public void Compare_WhenMessageFileVersionUnparseable_TreatsAsAbsentAndDoesNotThrow()
    {
        var parseable = WithProvenance(build: 20348, revision: 2000, messageFileVersion: "10.0.20348.900");
        var unparseable = WithProvenance(build: 20348, revision: 2000, messageFileVersion: "not-a-version");

        Assert.True(ProviderSourceRecency.Compare(parseable, unparseable) > 0);
    }

    private static ProviderDetails WithProvenance(int? build = null, int? revision = null, string? messageFileVersion = null) =>
        new()
        {
            ProviderName = "P",
            SourceOsBuild = build,
            SourceOsRevision = revision,
            MessageFileVersion = messageFileVersion
        };
}

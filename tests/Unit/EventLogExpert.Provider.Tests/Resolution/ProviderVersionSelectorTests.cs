// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Provider.Tests.Resolution;

public sealed class ProviderVersionSelectorTests
{
    private const string LogName = Constants.ApplicationLogName;
    private const string Provider = Constants.TestProviderLongName;

    [Fact]
    public void SelectMostComplete_EmptyDescriptionsDoNotCountTowardCompleteness()
    {
        var withCaptureGaps = ProviderWith((1, "real"), (2, ""), (3, ""));
        var fullyPopulated = ProviderWith((1, "a"), (2, "b"));

        var result = ProviderVersionSelector.SelectMostComplete([withCaptureGaps, fullyPopulated]);

        Assert.Same(fullyPopulated, result);
    }

    [Fact]
    public void SelectMostComplete_OnTrueTie_KeepsFirstCandidate()
    {
        // Equal completeness defers to candidate load order (the resolver loads newest-OS first).
        var first = ProviderWith((1, "same"));
        var second = ProviderWith((1, "same"));

        var result = ProviderVersionSelector.SelectMostComplete([first, second]);

        Assert.Same(first, result);
    }

    [Fact]
    public void SelectMostComplete_PrefersMoreNonEmptyDescriptions_AndReturnsThatCandidateUnchanged()
    {
        // Both candidates carry an empty VersionKey (the legacy/unstamped shape): selection is by completeness score,
        // NOT by version identity (a gate keyed on identity would collapse these), and the winner is returned as-is -
        // one version's whole provider, coherent, never a per-event blend.
        var sparse = ProviderWith((1, "only one"));
        var complete = ProviderWith((1, "one"), (2, "two"));

        Assert.Equal(string.Empty, sparse.VersionKey);
        Assert.Equal(string.Empty, complete.VersionKey);

        var result = ProviderVersionSelector.SelectMostComplete([sparse, complete]);

        Assert.Same(complete, result);
    }

    [Fact]
    public void SelectMostComplete_RecencyIsBelowCompleteness_MoreCompleteOlderSourceWins()
    {
        // A newer source that captured fewer descriptions must NOT beat an older, more-complete one.
        var newerButSparse = TiedProvider(build: 22621);
        var olderButComplete = ProviderWith((1, "one"), (2, "two"));
        olderButComplete.SourceOsBuild = 19041;

        var result = ProviderVersionSelector.SelectMostComplete([newerButSparse, olderButComplete]);

        Assert.Same(olderButComplete, result);
    }

    [Fact]
    public void SelectMostComplete_WhenBuildAndRevisionTie_PrefersNewerMessageFileVersion()
    {
        var olderFile = TiedProvider(build: 22621, revision: 1000, messageFileVersion: "10.0.22621.1");
        var newerFile = TiedProvider(build: 22621, revision: 1000, messageFileVersion: "10.0.22621.900");

        var result = ProviderVersionSelector.SelectMostComplete([olderFile, newerFile]);

        Assert.Same(newerFile, result);
    }

    [Fact]
    public void SelectMostComplete_WhenBuildTies_PrefersNewerRevision()
    {
        var olderRevision = TiedProvider(build: 22621, revision: 1000);
        var newerRevision = TiedProvider(build: 22621, revision: 2000);

        var result = ProviderVersionSelector.SelectMostComplete([olderRevision, newerRevision]);

        Assert.Same(newerRevision, result);
    }

    [Fact]
    public void SelectMostComplete_WhenCompletenessTies_PrefersNewerOsBuild()
    {
        // Older loads first; recency must override the load-order-keeps-first tiebreak and pick the newer build.
        var olderBuild = TiedProvider(build: 19041);
        var newerBuild = TiedProvider(build: 22621);

        var result = ProviderVersionSelector.SelectMostComplete([olderBuild, newerBuild]);

        Assert.Same(newerBuild, result);
    }

    [Fact]
    public void SelectMostComplete_WhenCompletenessTies_PresentProvenanceOutranksNull()
    {
        var noProvenance = TiedProvider();
        var withProvenance = TiedProvider(build: 19041);

        var result = ProviderVersionSelector.SelectMostComplete([noProvenance, withProvenance]);

        Assert.Same(withProvenance, result);
    }

    [Fact]
    public void SelectMostComplete_WhenDescriptionsTie_PrefersMoreLegacyMessages()
    {
        var fewerMessages = EventUtils.CreateProvider(Provider, [new MessageModel { ShortId = 1, RawId = 1, Text = "a" }]);
        var moreMessages = EventUtils.CreateProvider(
            Provider,
            [new MessageModel { ShortId = 1, RawId = 1, Text = "a" }, new MessageModel { ShortId = 2, RawId = 2, Text = "b" }]);

        var result = ProviderVersionSelector.SelectMostComplete([fewerMessages, moreMessages]);

        Assert.Same(moreMessages, result);
    }

    [Fact]
    public void SelectMostComplete_WhenMessageFileVersionUnparseable_DoesNotThrowAndKeepsLoadOrder()
    {
        var first = TiedProvider(build: 22621, revision: 1000, messageFileVersion: "not-a-version");
        var second = TiedProvider(build: 22621, revision: 1000, messageFileVersion: "also-bad");

        var result = ProviderVersionSelector.SelectMostComplete([first, second]);

        Assert.Same(first, result);
    }

    [Fact]
    public void SelectMostComplete_WhenNoCandidates_ReturnsNull() =>
        Assert.Null(ProviderVersionSelector.SelectMostComplete([]));

    [Fact]
    public void SelectMostComplete_WhenNonEmptyCountTies_PrefersLongerTotalDescription()
    {
        var shorter = ProviderWith((1, "ab"));
        var longer = ProviderWith((1, "abcdef"));

        var result = ProviderVersionSelector.SelectMostComplete([shorter, longer]);

        Assert.Same(longer, result);
    }

    [Fact]
    public void SelectMostComplete_WhenProvenanceAbsentOnAllCandidates_FallsBackToLoadOrder()
    {
        var first = TiedProvider();
        var second = TiedProvider();

        var result = ProviderVersionSelector.SelectMostComplete([first, second]);

        Assert.Same(first, result);
    }

    [Fact]
    public void SelectMostComplete_WhenSingleCandidate_ReturnsSameReference()
    {
        var only = ProviderWith((1, "desc"));

        Assert.Same(only, ProviderVersionSelector.SelectMostComplete([only]));
    }

    private static ProviderDetails ProviderWith(params (int Id, string? Description)[] events) =>
        EventUtils.CreateProvider(
            Provider,
            events: [.. events.Select(e => EventUtils.CreateEventModel(e.Id, e.Description, logName: LogName))]);

    private static ProviderDetails TiedProvider(int? build = null, int? revision = null, string? messageFileVersion = null)
    {
        var provider = EventUtils.CreateProvider(
            Provider,
            events: [EventUtils.CreateEventModel(1, "same", logName: LogName)]);

        provider.SourceOsBuild = build;
        provider.SourceOsRevision = revision;
        provider.MessageFileVersion = messageFileVersion;

        return provider;
    }
}

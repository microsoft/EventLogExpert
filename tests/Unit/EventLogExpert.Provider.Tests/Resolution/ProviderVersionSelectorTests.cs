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
    public void SelectMostComplete_WhenSingleCandidate_ReturnsSameReference()
    {
        var only = ProviderWith((1, "desc"));

        Assert.Same(only, ProviderVersionSelector.SelectMostComplete([only]));
    }

    private static ProviderDetails ProviderWith(params (int Id, string? Description)[] events) =>
        EventUtils.CreateProvider(
            Provider,
            events: [.. events.Select(e => EventUtils.CreateEventModel(e.Id, e.Description, logName: LogName))]);
}

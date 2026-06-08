// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;

namespace EventLogExpert.UI.Tests.DatabaseTools;

public sealed class FilterRegexFactoryTests
{
    [Fact]
    public void TryCreate_WhenPatternIsEmpty_ReturnsTrueAndNoRegexAndNoError()
    {
        var success = FilterRegexFactory.TryCreate(string.Empty, out var regex, out var error);

        // Empty pattern means "no filter": caller must distinguish from a malformed pattern.
        Assert.True(success);
        Assert.Null(regex);
        Assert.Null(error);
    }

    [Fact]
    public void TryCreate_WhenPatternIsInvalid_ReturnsFalseSetsErrorAndYieldsNullRegex()
    {
        // Unbalanced character class is a parse-time ArgumentException.
        var success = FilterRegexFactory.TryCreate("[unclosed", out var regex, out var error);

        Assert.False(success);
        Assert.Null(regex);
        // ex.Message text is localizable and version-sensitive; assert presence + non-empty rather than exact text.
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryCreate_WhenPatternIsNull_ReturnsTrueAndNoRegexAndNoError()
    {
        var success = FilterRegexFactory.TryCreate(null, out var regex, out var error);

        Assert.True(success);
        Assert.Null(regex);
        Assert.Null(error);
    }

    [Fact]
    public void TryCreate_WhenPatternIsValid_HasOneSecondMatchTimeout()
    {
        FilterRegexFactory.TryCreate("foo", out var regex, out _);

        // The timeout bound prevents catastrophic-backtracking patterns from hanging the
        // process. Pinning the exact 1-second value ensures a regression that quietly bumped this
        // to InfiniteMatchTimeout (or to many minutes) would fail this test.
        Assert.NotNull(regex);
        Assert.Equal(TimeSpan.FromSeconds(1), regex.MatchTimeout);
    }

    [Fact]
    public void TryCreate_WhenPatternIsValid_ReturnsTrueAndCaseInsensitiveRegex()
    {
        var success = FilterRegexFactory.TryCreate("microsoft-windows-.*", out var regex, out var error);

        // Case insensitivity is contractual: provider names like "Microsoft-Windows-AAD" must match.
        Assert.True(success);
        Assert.NotNull(regex);
        Assert.Null(error);
        Assert.Matches(regex, "Microsoft-Windows-AAD");
        Assert.Matches(regex, "MICROSOFT-WINDOWS-FOO");
        Assert.DoesNotMatch(regex, "OpenSSH");
    }
}

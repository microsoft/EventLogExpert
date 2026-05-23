// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using NSubstitute;

namespace EventLogExpert.EventDbTool.Tests;

public sealed class RegexHelperTests
{
    [Fact]
    public void TryCreate_WhenPatternIsEmpty_ReturnsTrueAndNoRegex()
    {
        // Arrange
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var success = RegexHelper.TryCreate(string.Empty, logger, out var regex);

        // Assert — empty pattern means "no filter", caller must distinguish from a malformed one.
        Assert.True(success);
        Assert.Null(regex);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public void TryCreate_WhenPatternIsInvalid_ReturnsFalseLogsErrorAndYieldsNullRegex()
    {
        // Arrange — unbalanced character class is a parse-time ArgumentException.
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var success = RegexHelper.TryCreate("[unclosed", logger, out var regex);

        // Assert
        Assert.False(success);
        Assert.Null(regex);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Invalid --filter regex") && h.ToString().Contains("[unclosed")));
    }

    [Fact]
    public void TryCreate_WhenPatternIsNull_ReturnsTrueAndNoRegex()
    {
        // Arrange
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var success = RegexHelper.TryCreate(null, logger, out var regex);

        // Assert
        Assert.True(success);
        Assert.Null(regex);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public void TryCreate_WhenPatternIsValid_HasOneSecondMatchTimeout()
    {
        // Arrange
        var logger = Substitute.For<ITraceLogger>();

        // Act
        RegexHelper.TryCreate("foo", logger, out var regex);

        // Assert — the timeout bound prevents catastrophic-backtracking patterns from hanging the
        // process. Pinning the exact 1-second value ensures a regression that quietly bumped this
        // to InfiniteMatchTimeout (or to many minutes) would fail this test.
        Assert.NotNull(regex);
        Assert.Equal(TimeSpan.FromSeconds(1), regex.MatchTimeout);
    }

    [Fact]
    public void TryCreate_WhenPatternIsValid_ReturnsTrueAndCaseInsensitiveRegex()
    {
        // Arrange
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var success = RegexHelper.TryCreate("microsoft-windows-.*", logger, out var regex);

        // Assert — case insensitivity is contractual: provider names like "Microsoft-Windows-AAD" must match.
        Assert.True(success);
        Assert.NotNull(regex);
        Assert.Matches(regex, "Microsoft-Windows-AAD");
        Assert.Matches(regex, "MICROSOFT-WINDOWS-FOO");
        Assert.DoesNotMatch(regex, "OpenSSH");
    }
}

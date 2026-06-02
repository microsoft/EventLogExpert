// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Windows.Tests.TestUtils.Constants;
using EventLogExpert.WindowsPlatform;
using Xunit;

namespace EventLogExpert.Windows.Tests;

public sealed class CommandLineToArgvWHelperTests
{
    [Fact]
    public void Parse_DriveRootStrippedOfTrailingBackslash_RoundTripsCorrectly()
    {
        var result = CommandLineToArgvWHelper.Parse(Constants.QuotedDriveRoot);

        Assert.Single(result);
        Assert.Equal(Constants.UnquotedDriveRoot, result[0]);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyArray()
    {
        Assert.Empty(CommandLineToArgvWHelper.Parse(""));
    }

    [Fact]
    public void Parse_MultipleQuotedAndUnquoted_PreservesEachToken()
    {
        var result = CommandLineToArgvWHelper.Parse(Constants.MultipleArgsCommandLine);

        Assert.Equal(3, result.Count);
        Assert.Equal(Constants.FirstArg, result[0]);
        Assert.Equal(Constants.MiddleArgWithSpaces, result[1]);
        Assert.Equal(Constants.LastArg, result[2]);
    }

    [Fact]
    public void Parse_QuotedPathWithSpaces_PreservesAsOneToken()
    {
        var result = CommandLineToArgvWHelper.Parse(Constants.QuotedPathWithSpaces);

        Assert.Single(result);
        Assert.Equal(Constants.PathWithSpacesUnquoted, result[0]);
    }

    [Fact]
    public void Parse_SingleUnquotedToken_ReturnsOneElement()
    {
        var result = CommandLineToArgvWHelper.Parse(Constants.UnquotedCLogsSampleEvtx);

        Assert.Single(result);
        Assert.Equal(Constants.UnquotedCLogsSampleEvtx, result[0]);
    }

    [Fact]
    public void Parse_UncPath_PreservesAsOneToken()
    {
        var result = CommandLineToArgvWHelper.Parse(Constants.QuotedUncPath);

        Assert.Single(result);
        Assert.Equal(Constants.UnquotedUncPath, result[0]);
    }
}

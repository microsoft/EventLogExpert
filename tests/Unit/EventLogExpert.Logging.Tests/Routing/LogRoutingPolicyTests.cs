// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Configuration;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Routing;

public sealed class LogRoutingPolicyTests
{
    [Fact]
    public void Constructor_NullOptions_Throws() =>
        Assert.Throws<ArgumentNullException>(static () => new LogRoutingPolicy(null!, LogLevel.Information));

    [Fact]
    public void FileMinimumFor_ConfiguredCategory_ReturnsOverride()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("DatabaseTools.Create"));
        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Offline.Wim"));
    }

    [Fact]
    public void FileMinimumFor_ExactConfiguredCategory_ReturnsOverride()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("DatabaseTools"));
    }

    [Fact]
    public void FileMinimumFor_LongestPrefixWins()
    {
        var options = new LoggingOptions
        {
            Sinks = new Dictionary<string, LogSinkOptions>(StringComparer.Ordinal)
            {
                [LoggingOptions.FileSink] = new LogSinkOptions
                {
                    Categories = new Dictionary<string, LogLevel>(StringComparer.Ordinal)
                    {
                        ["Offline"] = LogLevel.Warning,
                        ["Offline.Wim"] = LogLevel.Error
                    }
                }
            }
        };
        var policy = new LogRoutingPolicy(options, LogLevel.Information);

        Assert.Equal(LogLevel.Error, policy.FileMinimumFor("Offline.Wim.Extract"));
        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Offline.Iso"));
    }

    [Fact]
    public void FileMinimumFor_PartialSegment_DoesNotMatchOverride()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        Assert.Equal(LogLevel.Information, policy.FileMinimumFor("DatabaseToolsExtra"));
    }

    [Fact]
    public void FileMinimumFor_UnconfiguredCategory_FollowsGlobalBaseline()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        Assert.Equal(LogLevel.Information, policy.FileMinimumFor("App"));
        Assert.Equal(LogLevel.Information, policy.FileMinimumFor("Elevation.Ipc"));
    }

    [Fact]
    public void UiMinimumFor_Normal_IsInformation() =>
        Assert.Equal(LogLevel.Information, new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information).UiMinimumFor(verbose: false));

    [Fact]
    public void UiMinimumFor_Verbose_IsTrace() =>
        Assert.Equal(LogLevel.Trace, new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information).UiMinimumFor(verbose: true));

    [Fact]
    public void UpdateGlobalBaseline_MovesUnconfiguredCategories_ButConfiguredThrottleStaysAuthoritative()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        policy.UpdateGlobalBaseline(LogLevel.Error);

        Assert.Equal(LogLevel.Error, policy.FileMinimumFor("App"));

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("DatabaseTools.Create"));
    }
}

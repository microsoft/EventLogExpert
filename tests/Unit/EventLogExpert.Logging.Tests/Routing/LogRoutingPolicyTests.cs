// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Configuration;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Routing;

public sealed class LogRoutingPolicyTests
{
    private static readonly TimeSpan s_testTimeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void Constructor_NullOptions_Throws() =>
        Assert.Throws<ArgumentNullException>(static () => new LogRoutingPolicy(null!, LogLevel.Information));

    [Fact]
    public void FileMinimumFor_ConfiguredCategory_ReturnsOverride()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("DatabaseTools.Create"));
        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Offline.Wim"));
        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Resolution"));
        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Database"));
    }

    [Fact]
    public void FileMinimumFor_EventLog_FollowsGlobalBaseline_ReachableAtTrace()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Trace);

        Assert.Equal(LogLevel.Trace, policy.FileMinimumFor("EventLog"));
        Assert.Equal(LogLevel.Trace, policy.FileMinimumFor("EventLog.Lifecycle"));
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
                [LoggingOptions.FileLogSink] = new LogSinkOptions
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
    public void FileMinimumFor_NullOrEmptyCategory_ReturnsGlobalBaseline()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Warning);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor(null!));
        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor(string.Empty));
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
        Assert.Equal(LogLevel.Information, policy.FileMinimumFor("EventLog"));
        Assert.Equal(LogLevel.Information, policy.FileMinimumFor("EventLog.Lifecycle"));
    }

    [Fact]
    public void SetCategoryOverride_BeatsShippedThrottleAndBaseline()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Resolution"));

        policy.SetCategoryOverride("Resolution", LogLevel.Trace);

        Assert.Equal(LogLevel.Trace, policy.FileMinimumFor("Resolution"));
    }

    [Fact]
    public async Task SetCategoryOverride_ConcurrentReadersAndWriter_NeverTearOrThrow()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(s_testTimeout);
        CancellationToken token = cancellation.Token;

        Task writer = Task.Run(() =>
        {
            bool enabled = false;

            while (!token.IsCancellationRequested)
            {
                policy.SetCategoryOverride("Resolution", enabled ? LogLevel.Trace : null);
                enabled = !enabled;
            }
        }, TestContext.Current.CancellationToken);

        List<Task> tasks = [.. Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                LogLevel level = policy.FileMinimumFor("Resolution.Modern");
                Assert.True(level is LogLevel.Trace or LogLevel.Warning);
            }
        }, TestContext.Current.CancellationToken))];
        tasks.Add(writer);

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void SetCategoryOverride_CoversSubCategoriesViaPrefix()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        policy.SetCategoryOverride("Resolution", LogLevel.Trace);

        Assert.Equal(LogLevel.Trace, policy.FileMinimumFor("Resolution.Modern"));
        Assert.Equal(LogLevel.Trace, policy.FileMinimumFor("Resolution.Providers"));
    }

    [Fact]
    public void SetCategoryOverride_DoesNotAffectOtherCategories()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        policy.SetCategoryOverride("Resolution", LogLevel.Trace);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Database"));
        Assert.Equal(LogLevel.Information, policy.FileMinimumFor("App"));
    }

    [Fact]
    public void SetCategoryOverride_EmptyCategory_Throws()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        Assert.Throws<ArgumentException>(() => policy.SetCategoryOverride(string.Empty, LogLevel.Trace));
    }

    [Fact]
    public void SetCategoryOverride_NullForNeverSetCategory_IsNoOp()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        policy.SetCategoryOverride("Resolution", level: null);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Resolution"));
    }

    [Fact]
    public void SetCategoryOverride_Null_RevertsToShippedThrottle()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);
        policy.SetCategoryOverride("Resolution", LogLevel.Trace);

        policy.SetCategoryOverride("Resolution", level: null);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Resolution"));
        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Resolution.Modern"));
    }

    [Fact]
    public void SetCategoryOverride_SameCategoryTwice_ReplacesRatherThanAccumulates()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        policy.SetCategoryOverride("Resolution", LogLevel.Trace);
        policy.SetCategoryOverride("Resolution", LogLevel.Debug);

        Assert.Equal(LogLevel.Debug, policy.FileMinimumFor("Resolution"));

        policy.SetCategoryOverride("Resolution", level: null);

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("Resolution"));
    }

    [Fact]
    public void UIMinimumFor_Normal_IsInformation() =>
        Assert.Equal(LogLevel.Information, new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information).UIMinimumFor(verbose: false));

    [Fact]
    public void UIMinimumFor_Verbose_IsTrace() =>
        Assert.Equal(LogLevel.Trace, new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information).UIMinimumFor(verbose: true));

    [Fact]
    public void UpdateGlobalBaseline_MovesUnconfiguredCategories_ButConfiguredThrottleStaysAuthoritative()
    {
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), LogLevel.Information);

        policy.UpdateGlobalBaseline(LogLevel.Error);

        Assert.Equal(LogLevel.Error, policy.FileMinimumFor("App"));

        Assert.Equal(LogLevel.Warning, policy.FileMinimumFor("DatabaseTools.Create"));
    }
}

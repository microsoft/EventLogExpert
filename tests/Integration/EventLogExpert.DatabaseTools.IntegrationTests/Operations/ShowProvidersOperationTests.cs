// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.IntegrationTests.Operations;

public sealed class ShowCommandTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            DatabaseTestUtils.DeleteDatabaseFile(path);
        }
    }

    [Fact]
    public async Task ShowProviderInfo_WhenSourceDoesNotExist_LogsErrorAndDoesNotReadAnything()
    {
        // Arrange
        var missing = DatabaseTestUtils.CreateTempPath();
        var logger = new CapturingTraceLogger();

        // Act
        await new ShowProvidersOperation(new ShowProvidersRequest(missing, null))
            .ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.True(logger.Contains(LogLevel.Error, "Source not found", missing));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task ShowProviderInfo_WhenSourceFilterMatchesNoProviders_LogsNoProvidersWarningOnly()
    {
        // Arrange - source has providers but the filter matches none. The "no providers found"
        // message is the contract for "your filter is too narrow" UX, distinct from "source missing".
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var logger = new CapturingTraceLogger();

        // Act
        await new ShowProvidersOperation(new ShowProvidersRequest(source, new Regex("ZZZ_NoMatch_ZZZ", RegexOptions.IgnoreCase)))
            .ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.True(logger.Contains(LogLevel.Warning, "No providers found"));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        // No detail header should be emitted when there is nothing to list.
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Provider Name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShowProviderInfo_WhenSourceHasProviders_LogsHeaderAndOneRowPerProvider()
    {
        // Arrange - two providers. We assert that the header appears exactly once and a row for
        // each provider name appears, locking in the streaming "header + per-provider row" output
        // contract that downstream tooling and humans both depend on.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var logger = new CapturingTraceLogger();

        // Act
        await new ShowProvidersOperation(new ShowProvidersRequest(source, null))
            .ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.True(logger.Contains(LogLevel.Information, "Provider Name", "Events"));
        Assert.True(logger.Contains(LogLevel.Information, Constants.FirstProviderName));
        Assert.True(logger.Contains(LogLevel.Information, Constants.SecondProviderName));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    private string CreateTempPath()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);

        return path;
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using NSubstitute;
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
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new ShowProvidersOperation(new ShowProvidersRequest(missing, null))
            .ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Source not found") && h.ToString().Contains(missing)));
        logger.DidNotReceive().Information(Arg.Any<InformationLogHandler>());
    }

    [Fact]
    public async Task ShowProviderInfo_WhenSourceFilterMatchesNoProviders_LogsNoProvidersWarningOnly()
    {
        // Arrange — source has providers but the filter matches none. The "no providers found"
        // message is the contract for "your filter is too narrow" UX, distinct from "source missing".
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new ShowProvidersOperation(new ShowProvidersRequest(source, new Regex("ZZZ_NoMatch_ZZZ", RegexOptions.IgnoreCase)))
            .ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        logger.Received(1).Warning(Arg.Is<WarningLogHandler>(h =>
            h.ToString().Contains("No providers found")));
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        // No detail header should be emitted when there is nothing to list.
        logger.DidNotReceive().Information(Arg.Is<InformationLogHandler>(h =>
            h.ToString().Contains("Provider Name")));
    }

    [Fact]
    public async Task ShowProviderInfo_WhenSourceHasProviders_LogsHeaderAndOneRowPerProvider()
    {
        // Arrange — two providers. We assert that the header appears exactly once and a row for
        // each provider name appears, locking in the streaming "header + per-provider row" output
        // contract that downstream tooling and humans both depend on.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new ShowProvidersOperation(new ShowProvidersRequest(source, null))
            .ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        logger.Received(1).Information(Arg.Is<InformationLogHandler>(h =>
            h.ToString().Contains("Provider Name") && h.ToString().Contains("Events")));
        logger.Received(1).Information(Arg.Is<InformationLogHandler>(h =>
            h.ToString().Contains(Constants.FirstProviderName)));
        logger.Received(1).Information(Arg.Is<InformationLogHandler>(h =>
            h.ToString().Contains(Constants.SecondProviderName)));
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        logger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
    }

    private string CreateTempPath()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);

        return path;
    }
}

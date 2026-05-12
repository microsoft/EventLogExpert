// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.EventDbTool.IntegrationTests.TestUtils;
using EventLogExpert.EventDbTool.IntegrationTests.TestUtils.Constants;
using EventLogExpert.Eventing.Logging;
using NSubstitute;

namespace EventLogExpert.EventDbTool.IntegrationTests;

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
    public void ShowProviderInfo_WhenFilterRegexInvalid_LogsErrorAndReturns()
    {
        // Arrange — pass a source so we never hit the local-machine path (non-deterministic).
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var logger = Substitute.For<ITraceLogger>();

        // Act
        new ShowCommand(logger).ShowProviderInfo(source, filter: "[unclosed");

        // Assert
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Invalid --filter regex")));
        logger.DidNotReceive().Information(Arg.Any<InformationLogHandler>());
        logger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public void ShowProviderInfo_WhenSourceDoesNotExist_LogsErrorAndDoesNotReadAnything()
    {
        // Arrange
        var missing = DatabaseTestUtils.CreateTempPath(".db");
        var logger = Substitute.For<ITraceLogger>();

        // Act
        new ShowCommand(logger).ShowProviderInfo(missing, filter: null);

        // Assert
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Source not found") && h.ToString().Contains(missing)));
        logger.DidNotReceive().Information(Arg.Any<InformationLogHandler>());
    }

    [Fact]
    public void ShowProviderInfo_WhenSourceFilterMatchesNoProviders_LogsNoProvidersWarningOnly()
    {
        // Arrange — source has providers but the filter matches none. The "no providers found"
        // message is the contract for "your filter is too narrow" UX, distinct from "source missing".
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var logger = Substitute.For<ITraceLogger>();

        // Act
        new ShowCommand(logger).ShowProviderInfo(source, filter: "ZZZ_NoMatch_ZZZ");

        // Assert
        logger.Received(1).Warning(Arg.Is<WarningLogHandler>(h =>
            h.ToString().Contains("No providers found")));
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        // No detail header should be emitted when there is nothing to list.
        logger.DidNotReceive().Information(Arg.Is<InformationLogHandler>(h =>
            h.ToString().Contains("Provider Name")));
    }

    [Fact]
    public void ShowProviderInfo_WhenSourceHasProviders_LogsHeaderAndOneRowPerProvider()
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
        new ShowCommand(logger).ShowProviderInfo(source, filter: null);

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

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.ProviderDatabase.Context;
using NSubstitute;

namespace EventLogExpert.DatabaseTools.IntegrationTests.Operations;

public sealed class DiffDatabaseCommandTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    [Fact]
    public async Task DiffDatabase_CopiesNewVersionOfNameAlsoPresentInFirstSource()
    {
        var first = CreateTempDb();
        var second = CreateTempDb();
        var output = CreateTempDb();

        var firstV1 = DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName);
        firstV1.VersionKey = "vk1";
        DatabaseTestUtils.CreateV4Database(first, firstV1);

        var secondV1 = DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName);
        secondV1.VersionKey = "vk1";
        var secondV2 = DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName);
        secondV2.VersionKey = "vk2";
        DatabaseTestUtils.CreateV4Database(second, secondV1, secondV2);

        File.Delete(output);

        var logger = Substitute.For<ITraceLogger>();

        // The diff skips by identity, so the second source's new vk2 version of a name also in the first source (only
        // as vk1) is still treated as "missing from first" and copied. A name-level skip would drop it entirely.
        await new DiffDatabaseOperation(new DiffDatabaseRequest(first, second, output)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(File.Exists(output), "Diff should have created the output database.");

        await using var verify = new ProviderDbContext(output, readOnly: true, ensureCreated: false);
        var rows = verify.ProviderDetails.ToList();
        var copied = Assert.Single(rows);
        Assert.Equal(Constants.SharedProviderName, copied.ProviderName);
        Assert.Equal("vk2", copied.VersionKey);
    }

    [Fact]
    public async Task DiffDatabase_PreservesResolvedFromOwningPublisher()
    {
        // Arrange — first source has Shared, second source has Shared and Second.
        // The diff output should contain only Second, with its ResolvedFromOwningPublisher value
        // intact. This locks in the Add(details) projection in DiffDatabase: a hand-projection that
        // forgot ResolvedFromOwningPublisher (or any future ProviderDetails member) would fail this.
        var first = CreateTempDb();
        var second = CreateTempDb();
        var output = CreateTempDb();

        DatabaseTestUtils.CreateV4Database(first,
            DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName));

        DatabaseTestUtils.CreateV4Database(second,
            DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName, Constants.OwningPublisherName));

        File.Delete(output);

        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new DiffDatabaseOperation(new DiffDatabaseRequest(first, second, output)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(output), "Diff should have created the output database.");

        await using var verify = new ProviderDbContext(output, readOnly: true, ensureCreated: false);
        var rows = verify.ProviderDetails.ToList();
        var copied = Assert.Single(rows);
        Assert.Equal(Constants.SecondProviderName, copied.ProviderName);
        Assert.Equal(Constants.OwningPublisherName, copied.ResolvedFromOwningPublisher);
    }

    [Fact]
    public async Task DiffDatabase_WithObsoleteSecondSource_AbortsWithoutCreatingOutput()
    {
        // Arrange — V3 schema in second source. Diff aborts on stale-but-known schema too.
        var first = CreateTempDb();
        var second = CreateTempDb();
        var output = CreateTempDb();

        DatabaseTestUtils.CreateV4Database(first,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        DatabaseTestUtils.CreateV3Database(second);

        File.Delete(output);

        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new DiffDatabaseOperation(new DiffDatabaseRequest(first, second, output)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.False(File.Exists(output), "Diff must not create an output database when a source is obsolete.");
        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("schema v3") && handler.ToString().Contains(second) && handler.ToString().Contains("upgrade")));
    }

    [Fact]
    public async Task DiffDatabase_WithUnknownFirstSource_AbortsWithoutCreatingOutput()
    {
        // Arrange — without the upfront ValidateSourceSchemas check, the schema-rejected first
        // source would silently yield zero providers, the second source's full contents would be
        // copied to output as "missing from first", and the user would get a wrong result with
        // only a single error log line buried earlier in the output.
        var first = CreateTempDb();
        var second = CreateTempDb();
        var output = CreateTempDb();

        DatabaseTestUtils.CreateUnknownShapeDatabase(first);
        DatabaseTestUtils.CreateV4Database(second,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        File.Delete(output);

        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new DiffDatabaseOperation(new DiffDatabaseRequest(first, second, output)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.False(File.Exists(output), "Diff must not create an output database when a source is invalid.");
        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") && handler.ToString().Contains(first)));
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            DatabaseTestUtils.DeleteDatabaseFile(path);
        }
    }

    private string CreateTempDb()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);
        return path;
    }
}

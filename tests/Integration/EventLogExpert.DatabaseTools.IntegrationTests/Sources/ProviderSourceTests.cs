// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Provider.Resolution;
using NSubstitute;

namespace EventLogExpert.DatabaseTools.IntegrationTests.Sources;

public sealed class ProviderSourceTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];
    private readonly List<string> _tempPaths = [];

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            DatabaseTestUtils.DeleteDatabaseFile(path);
        }

        foreach (var dir in _tempDirectories)
        {
            DatabaseTestUtils.DeleteDirectoryRecursive(dir);
        }
    }

    [Fact]
    public async Task LoadProviderIdentities_NonAsciiCaseVariantNames_ReturnsDeterministicOrder()
    {
        // Two names differing only by non-ASCII case (U+00C9 vs U+00E9) are distinct identities but tie under the
        // OrdinalIgnoreCase primary sort; the ordinal tiebreak must order them deterministically (U+00C9 < U+00E9)
        // regardless of the source HashSet's iteration order.
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateV4Database(dbPath,
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00e9"),
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00c9"));
        var logger = Substitute.For<ITraceLogger>();

        var identities = await ProviderSource.LoadProviderIdentitiesAsync(
            dbPath, logger, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Provider-\u00c9", "Provider-\u00e9"],
            identities.Select(identity => identity.ProviderName).ToList());
    }

    [Fact]
    public async Task LoadProviders_NonAsciiCaseVariantNamesInOneDatabase_LoadsBoth()
    {
        // Arrange - SQLite's NOCASE primary-key collation folds only ASCII, so provider names differing solely by
        // non-ASCII case (here U+00E9 vs U+00C9) are DISTINCT rows that coexist in one database. The reload-by-name
        // path must de-duplicate names ordinally; folding them via OrdinalIgnoreCase or the identity HashSet (both of
        // which fold all of Unicode) would collapse the two names to one and silently drop a row.
        var dbPath = CreateTempDb();
        var lower = DatabaseTestUtils.BuildProviderDetails("Provider-\u00e9");
        var upper = DatabaseTestUtils.BuildProviderDetails("Provider-\u00c9");
        DatabaseTestUtils.CreateV4Database(dbPath, lower, upper);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var loaded = new List<ProviderDetails>();

        await foreach (var provider in ProviderSource.LoadProvidersAsync(
            dbPath, logger, cancellationToken: TestContext.Current.CancellationToken))
        {
            loaded.Add(provider);
        }

        // Assert - both case variants load; neither is collapsed away.
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, provider => provider.ProviderName == "Provider-\u00e9");
        Assert.Contains(loaded, provider => provider.ProviderName == "Provider-\u00c9");
    }

    [Fact]
    public async Task LoadProviders_SameNameDistinctVersionsAcrossFiles_LoadsBothVersions()
    {
        // Arrange - two source files carry the same provider name but distinct VersionKeys, i.e. different provider
        // versions. The cross-file `seen` dedup is keyed by identity (name, version), not name alone, so both must
        // load rather than the second file's version being dropped as a name-duplicate. VersionKey is empty in
        // production today; this guards the behavior once content hashing makes versions coexist.
        var dir = CreateTempDir();
        var first = Path.Combine(dir, "first.db");
        var second = Path.Combine(dir, "second.db");

        var firstVersion = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        firstVersion.VersionKey = "vk1";
        var secondVersion = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        secondVersion.VersionKey = "vk2";

        DatabaseTestUtils.CreateV4Database(first, firstVersion);
        DatabaseTestUtils.CreateV4Database(second, secondVersion);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var loaded = new List<ProviderDetails>();

        await foreach (var provider in ProviderSource.LoadProvidersAsync(
            dir, logger, cancellationToken: TestContext.Current.CancellationToken))
        {
            loaded.Add(provider);
        }

        // Assert
        Assert.Equal(2, loaded.Count);
        Assert.Equal(["vk1", "vk2"], loaded.Select(p => p.VersionKey).OrderBy(v => v, StringComparer.Ordinal).ToArray());
        Assert.All(loaded, provider => Assert.Equal(Constants.FirstProviderName, provider.ProviderName));
    }

    [Fact]
    public async Task ValidateSourceSchemas_WithCurrentSchemaDatabase_ReturnsTrue()
    {
        // Arrange
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateV4Database(dbPath, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = await ProviderSource.ValidateSourceSchemasAsync(dbPath, logger, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task ValidateSourceSchemas_WithDirectoryContainingAnyInvalidDatabase_ReturnsFalse()
    {
        // Arrange - one valid, one Unknown shape. The whole directory must fail.
        var dir = CreateTempDir();
        var good = Path.Combine(dir, "good.db");
        var bad = Path.Combine(dir, "bad.db");
        DatabaseTestUtils.CreateV4Database(good, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        DatabaseTestUtils.CreateUnknownShapeDatabase(bad);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = await ProviderSource.ValidateSourceSchemasAsync(dir, logger, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") &&
            handler.ToString().Contains(bad)));
    }

    [Fact]
    public async Task ValidateSourceSchemas_WithDirectoryMixingDbAndEvtx_OnlyValidatesDb()
    {
        // Arrange - only the .db files participate in schema validation; the evtx is ignored.
        var dir = CreateTempDir();
        var dbPath = Path.Combine(dir, "valid.db");
        var evtxPath = Path.Combine(dir, "ignored.evtx");
        DatabaseTestUtils.CreateV4Database(dbPath, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        File.WriteAllBytes(evtxPath, new byte[] { 0x00 });
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = await ProviderSource.ValidateSourceSchemasAsync(dir, logger, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task ValidateSourceSchemas_WithDirectoryOfValidDatabases_ReturnsTrue()
    {
        // Arrange
        var dir = CreateTempDir();
        var first = Path.Combine(dir, "first.db");
        var second = Path.Combine(dir, "second.db");
        DatabaseTestUtils.CreateV4Database(first, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        DatabaseTestUtils.CreateV4Database(second, DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = await ProviderSource.ValidateSourceSchemasAsync(dir, logger, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task ValidateSourceSchemas_WithEvtxFile_IsSkipped()
    {
        // Arrange - .evtx files have no schema concept; ValidateSourceSchemas should ignore them
        // entirely (the evtx-specific load path goes through MtaProviderSource elsewhere).
        var evtxPath = CreateTempPath(".evtx");
        File.WriteAllBytes(evtxPath, new byte[] { 0x00 });
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = await ProviderSource.ValidateSourceSchemasAsync(evtxPath, logger, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task ValidateSourceSchemas_WithObsoleteV3Database_ReturnsFalseAndLogsError()
    {
        // Arrange
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateV3Database(dbPath);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = await ProviderSource.ValidateSourceSchemasAsync(dbPath, logger, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("schema v3") &&
            handler.ToString().Contains(dbPath) &&
            handler.ToString().Contains("upgrade")));
    }

    [Fact]
    public async Task ValidateSourceSchemas_WithUnknownSchemaDatabase_ReturnsFalseAndLogsError()
    {
        // Arrange
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateUnknownShapeDatabase(dbPath);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = await ProviderSource.ValidateSourceSchemasAsync(dbPath, logger, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") &&
            handler.ToString().Contains(dbPath)));
    }

    private string CreateTempDb()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);
        return path;
    }

    private string CreateTempDir()
    {
        var dir = DatabaseTestUtils.CreateTempDirectory();
        _tempDirectories.Add(dir);
        return dir;
    }

    private string CreateTempPath(string extension)
    {
        var path = DatabaseTestUtils.CreateTempPath(extension);
        _tempPaths.Add(path);
        return path;
    }
}

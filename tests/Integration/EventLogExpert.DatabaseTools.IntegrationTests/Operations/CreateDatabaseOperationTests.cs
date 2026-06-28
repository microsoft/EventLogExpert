// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.ProviderDatabase.Context;
using EventLogExpert.ProviderDatabase.Hashing;
using NSubstitute;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.IntegrationTests.Operations;

public sealed class CreateDatabaseCommandTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<string> _tempPaths = [];

    [Fact]
    public async Task CreateDatabase_FolderSourceWithSameContentUnderDifferentKeys_CollapsesInsteadOfColliding()
    {
        // Two source files hold the SAME provider with byte-identical content but different stored VersionKeys (one
        // unstamped legacy row, one already hashed). The cross-file dedup keys on the source key, so both survive
        // load, then both re-hash to the same key. Without a post-stamp guard they would collide on the composite
        // primary key and abort the create; they must instead collapse to one row.
        var dir = CreateTempDir();

        var unstamped = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        var stamped = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        stamped.VersionKey = VersionKeyCalculator.Compute(stamped);

        DatabaseTestUtils.CreateV4Database(Path.Combine(dir, "a.db"), unstamped);
        DatabaseTestUtils.CreateV4Database(Path.Combine(dir, "b.db"), stamped);

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, dir, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(File.Exists(path), "Create should have produced a database (collapsed, not aborted).");

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var single = Assert.Single(verify.ProviderDetails.ToList());
        Assert.Equal(Constants.FirstProviderName, single.ProviderName);
        Assert.StartsWith("vk1:", single.VersionKey);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_StampsContentHashVersionKey()
    {
        // A source provider with an empty (unstamped) VersionKey must come out of create with its content hash
        // stamped, so the composite (name, version) primary key can hold genuinely different versions of a provider
        // and identical providers collapse to one row.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        await using var context = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var created = context.ProviderDetails.Single();

        Assert.Equal(Constants.FirstProviderName, created.ProviderName);
        Assert.StartsWith("vk1:", created.VersionKey);
        Assert.Equal(VersionKeyCalculator.Compute(created), created.VersionKey);
    }

    [Fact]
    public async Task CreateDatabase_WhenBothSourceAndOfflineImageGiven_LogsErrorAndDoesNotCreateFile()
    {
        // Source and offline image are mutually exclusive. The mutual-exclusivity check must fire BEFORE source
        // validation, so the user gets the clear "one or the other" message rather than a confusing "source not found".
        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(
            path, SourcePath: @"C:\src.db", FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\"))
            .ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "No file should be written when a source and an offline image are both given.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h => h.ToString().Contains("source OR an offline image")));
    }

    [Fact]
    public async Task CreateDatabase_WhenExtensionNotDb_LogsErrorAndDoesNotCreateFile()
    {
        // Arrange
        var path = DatabaseTestUtils.CreateTempPath(".txt");
        _tempPaths.Add(path);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, null, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.False(File.Exists(path), "No file should be written when the extension is wrong.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("File extension must be .db")));
    }

    [Fact]
    public async Task CreateDatabase_WhenFilterMatchesNoProviders_DoesNotLeaveEmptyDatabaseOnDisk()
    {
        // Arrange — source has First+Second, filter excludes both. The command must not leave an
        // empty .db file behind, because a downstream consumer would read it as "this collection
        // has zero providers" instead of "this provider set was never collected".
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, new Regex("ZZZ_NoMatch_ZZZ", RegexOptions.IgnoreCase), null)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.False(File.Exists(path), "No file should be written when zero providers were resolved.");
        logger.Received(1).Warning(Arg.Is<WarningLogHandler>(h =>
            h.ToString().Contains("No provider details could be resolved") &&
            h.ToString().Contains("Database was not created")));
        // No errors should have been logged on this path; an extra error here would be a UX regression.
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenOfflineImageKindIsWim_LogsNotSupportedErrorAndDoesNotCreateFile()
    {
        // WIM extraction is a later phase; v1 supports only a directory (mounted volume / extracted folder).
        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(
            path, SourcePath: null, FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\",
            ImageKind: OfflineImageKind.Wim)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "WIM offline extraction is not yet supported; no file should be written.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h => h.ToString().Contains("not yet supported")));
    }

    [Fact]
    public async Task CreateDatabase_WhenProviderCountCrossesBatchSize_PersistsEveryProviderWithoutErrors()
    {
        // Arrange — exercises the mid-stream FlushHeaderAndBuffer path: at 100 providers the buffer
        // is flushed, the DbContext is materialized, and subsequent providers are appended in
        // BatchSize=100 chunks. 101 providers guarantees we cross the boundary AND continue beyond
        // it, so a regression in the "after first flush" branch would lose providers.
        const int ProviderCount = 101;
        var source = CreateTempPath();
        var providers = Enumerable.Range(0, ProviderCount)
            .Select(i => DatabaseTestUtils.BuildProviderDetails($"Provider-{i:D4}"))
            .ToArray();
        DatabaseTestUtils.CreateV4Database(source, providers);

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(path));

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        Assert.Equal(ProviderCount, verify.ProviderDetails.Count());
        var firstName = verify.ProviderDetails.OrderBy(r => r.ProviderName).First().ProviderName;
        var lastName = verify.ProviderDetails.OrderBy(r => r.ProviderName).Last().ProviderName;
        Assert.Equal("Provider-0000", firstName);
        Assert.Equal($"Provider-{ProviderCount - 1:D4}", lastName);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        logger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenSkipProvidersInFileExcludesAll_DoesNotLeaveEmptyDatabaseOnDisk()
    {
        // Arrange — distinct contract path from the filter case: the skip-source contains every
        // provider in the source, leaving zero to write. The "no empty .db" guarantee must hold
        // here too; a regression in the skip-set integration could reintroduce the empty stub.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var skipSource = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(skipSource,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, skipSource)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.False(File.Exists(path), "No file should be written when the skip-set excludes all providers.");
        logger.Received(1).Warning(Arg.Is<WarningLogHandler>(h =>
            h.ToString().Contains("No provider details could be resolved") &&
            h.ToString().Contains("Database was not created")));
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenSkipProvidersInFileResolves_ExcludesThoseProvidersFromOutput()
    {
        // Arrange — source has First+Second+Shared. Skip-source has Shared. Output must contain
        // First+Second only, exercising the skip-set integration with the streaming write path.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName));

        var skipSource = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(skipSource,
            DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, skipSource)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(path));

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var names = verify.ProviderDetails.Select(r => r.ProviderName).OrderBy(n => n).ToList();
        Assert.Equal(new[] { Constants.FirstProviderName, Constants.SecondProviderName }, names);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        logger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenSkipProvidersInFileSourceDoesNotExist_LogsErrorAndDoesNotCreateFile()
    {
        // Arrange — valid source but invalid skip-source. Validation order matters: a missing skip
        // file must fail BEFORE we begin writing the output database, so an empty stub is never
        // left behind.
        var path = CreateTempPath();
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var missingSkipSource = DatabaseTestUtils.CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, missingSkipSource)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.False(File.Exists(path), "No file should be written when the skip-source is missing.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Source not found") && h.ToString().Contains(missingSkipSource)));
    }

    [Fact]
    public async Task CreateDatabase_WhenSourceDoesNotExist_LogsErrorAndDoesNotCreateFile()
    {
        // Arrange
        var path = CreateTempPath();
        var missingSource = DatabaseTestUtils.CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, missingSource, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.False(File.Exists(path), "No file should be written when the source is missing.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Source not found") && h.ToString().Contains(missingSource)));
    }

    [Fact]
    public async Task CreateDatabase_WhenSourceProvidersResolved_PersistsAllProvidersAndPreservesOwningPublisher()
    {
        // Arrange — one provider has ResolvedFromOwningPublisher set; this round-trips through the
        // full streaming write path and we verify the persisted DB matches the source contents.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName, Constants.OwningPublisherName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(path), "Output database should be created when providers are resolved.");

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var rows = verify.ProviderDetails.OrderBy(r => r.ProviderName).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(Constants.FirstProviderName, rows[0].ProviderName);
        Assert.Null(rows[0].ResolvedFromOwningPublisher);
        Assert.Equal(Constants.SecondProviderName, rows[1].ProviderName);
        Assert.Equal(Constants.OwningPublisherName, rows[1].ResolvedFromOwningPublisher);
        // Success path must not surface any errors or warnings; a regression that warned spuriously
        // (e.g., "no providers resolved" reaching the success branch) would degrade operator trust.
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        logger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenTargetFileAlreadyExists_LogsErrorAndDoesNotOverwrite()
    {
        // Arrange — target file already exists with sentinel bytes; the command must not overwrite or truncate.
        var path = CreateTempPath();
        var sentinel = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        File.WriteAllBytes(path, sentinel);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, null, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        // Assert
        Assert.Equal(sentinel, File.ReadAllBytes(path));
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("file already exists") && h.ToString().Contains(path)));
    }

    [Fact]
    public async Task CreateDatabase_WhenWimIndexGivenForDirectoryImage_LogsErrorAndDoesNotCreateFile()
    {
        // WimIndex only means anything for a WIM image; supplying it for a directory image is rejected rather than
        // silently ignored, so the request can't quietly do something other than what the caller asked.
        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(
            path, SourcePath: null, FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\",
            WimIndex: 1)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "WimIndex applies only to WIM images; no file should be written.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h => h.ToString().Contains("WimIndex applies only to WIM")));
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            DatabaseTestUtils.DeleteDatabaseFile(path);
        }

        foreach (var dir in _tempDirs)
        {
            DatabaseTestUtils.DeleteDirectoryRecursive(dir);
        }
    }

    private string CreateTempDir()
    {
        var dir = DatabaseTestUtils.CreateTempDirectory();
        _tempDirs.Add(dir);

        return dir;
    }

    private string CreateTempPath()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);

        return path;
    }
}

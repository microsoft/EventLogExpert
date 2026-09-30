// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Localization;
using EventLogExpert.Provider.Database.Context;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.IntegrationTests.Operations;

public sealed class MergeDatabaseCommandTests : IDisposable
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
    public async Task MergeDatabaseWithOverwrite_NonAsciiCaseVariantNames_DoesNotCollide()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        // SQLite NOCASE folds only ASCII, so non-ASCII case variants must remain distinct identities.
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00c4"),
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00e4"));
        DatabaseTestUtils.CreateV4Database(target,
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00c4"),
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00e4"));

        var logger = new CapturingTraceLogger();

        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, true)).ExecuteAsync(logger, null, CancellationToken.None);

        using var context = new ProviderDbContext(target, readOnly: true, ensureCreated: false);
        var names = context.ProviderDetails
            .Select(p => p.ProviderName)
            .AsEnumerable()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Provider-\u00c4", "Provider-\u00e4"], names);
    }

    [Fact]
    public async Task MergeDatabaseWithOverwrite_RemovesOnlyCollidingVersionAndKeepsOtherTargetVersion()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        var targetV1 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        targetV1.VersionKey = "vk1";
        var targetV2 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        targetV2.VersionKey = "vk2";
        DatabaseTestUtils.CreateV4Database(target, targetV1, targetV2);

        var sourceV1 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        sourceV1.VersionKey = "vk1";
        DatabaseTestUtils.CreateV4Database(source, sourceV1);

        var logger = new CapturingTraceLogger();

        // Overwrite must delete by composite identity; a name-level delete would drop target vk2.
        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, true)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(["vk1", "vk2"], ReadVersionKeys(target, Constants.FirstProviderName));
    }

    [Fact]
    public async Task MergeDatabaseWithoutOverwrite_CopiesNewVersionOfExistingProviderName()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        var targetV1 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        targetV1.VersionKey = "vk1";
        DatabaseTestUtils.CreateV4Database(target, targetV1);

        var sourceV1 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        sourceV1.VersionKey = "vk1";
        var sourceV2 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        sourceV2.VersionKey = "vk2";
        DatabaseTestUtils.CreateV4Database(source, sourceV1, sourceV2);

        var logger = new CapturingTraceLogger();

        // Without overwrite, skip by composite identity; a name-level skip would drop vk2.
        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(["vk1", "vk2"], ReadVersionKeys(target, Constants.FirstProviderName));

        // The copied-versions message routes through the ICU PluralCount transport: one copied version selects the
        // singular branch and renders the §3I-corrected "version" (not "version(s)"), not the raw ICU pattern.
        var copied = Assert.Single(logger.Entries, entry => entry.Key == "DatabaseTools_Op_MergeCopiedVersions");
        Assert.Equal(1L, copied.PluralCount);

        // Copy-robust: the emitted message equals a fresh neutral render of this key at the emitted count and renders
        // formatted text (no raw ICU pattern). The one/other branches must also genuinely differ, so a regression that
        // reverts the §3I "version"/"versions" correction to identical branches is caught.
        var neutral = new SharedResourceNeutralResolver();
        static string Normalize(string value) => Regex.Replace(value, @"\d+(?:,\d{3})*(?:\.\d+)?", "#");

        Assert.Equal(neutral.Resolve("DatabaseTools_Op_MergeCopiedVersions", [], 1L), copied.Message);
        Assert.DoesNotContain("{count", copied.Message, StringComparison.Ordinal);
        Assert.NotEqual(
            Normalize(neutral.Resolve("DatabaseTools_Op_MergeCopiedVersions", [], 1L)),
            Normalize(neutral.Resolve("DatabaseTools_Op_MergeCopiedVersions", [], 2L)));

        // The count must render as a standalone token in BOTH branches - "1" in the singular and a distinctive value in
        // the plural - so a {count} dropped from EITHER branch is caught (the noun inflection alone cannot mask it).
        Assert.Matches(@"(?<![\d,])1(?![\d,])", neutral.Resolve("DatabaseTools_Op_MergeCopiedVersions", [], 1L));
        Assert.Contains("987654", neutral.Resolve("DatabaseTools_Op_MergeCopiedVersions", [], 987654L), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MergeDatabase_WithCorruptTarget_LogsErrorWithoutThrowing()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        File.WriteAllBytes(target, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        var logger = new CapturingTraceLogger();

        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(logger.Contains(LogLevel.Error, "Failed to merge into database", target));
    }

    [Fact]
    public async Task MergeDatabase_WithEmptyTargetFile_LogsUnrecognizedSchemaWithoutCreatingTables()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        File.WriteAllBytes(target, []);

        var logger = new CapturingTraceLogger();

        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(logger.Contains(LogLevel.Error, "unrecognized schema", target));
    }

    [Fact]
    public async Task MergeDatabase_WithSourceNeedingUpgrade_FailsInsteadOfReportingSuccess()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        // Pre-stamp sources must fail clearly instead of reporting success while copying nothing.
        DatabaseTestUtils.CreateV3Database(source);
        DatabaseTestUtils.CreateV4Database(target, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var logger = new CapturingTraceLogger();

        var outcome = await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false))
            .ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        Assert.True(logger.Contains(LogLevel.Error, source));
    }

    [Fact]
    public async Task MergeDatabase_WithUnknownSchemaTarget_LogsErrorWithoutThrowing()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        DatabaseTestUtils.CreateUnknownShapeDatabase(target);

        var logger = new CapturingTraceLogger();

        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(logger.Contains(LogLevel.Error, "unrecognized schema", target));
    }

    [Fact]
    public async Task MergeDatabase_WithZeroSourceProviders_FailsInsteadOfReportingSuccess()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        // Zero source providers must fail truthfully instead of reporting success while modifying nothing.
        DatabaseTestUtils.CreateV4Database(source);
        DatabaseTestUtils.CreateV4Database(target, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var logger = new CapturingTraceLogger();

        var operation = new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false));
        var outcome = await operation.ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        Assert.Equal(DatabaseToolsLogKeys.MergeFailureNoProvidersDiscovered, operation.FailureSummary?.Key);
        Assert.True(logger.Contains(LogLevel.Warning, "No providers were discovered in the source"));
    }

    private static string[] ReadVersionKeys(string databasePath, string providerName)
    {
        using var context = new ProviderDbContext(databasePath, readOnly: true, ensureCreated: false);

        return context.ProviderDetails
            .Where(p => p.ProviderName == providerName)
            .Select(p => p.VersionKey)
            .AsEnumerable()
            .OrderBy(versionKey => versionKey, StringComparer.Ordinal)
            .ToArray();
    }

    private string CreateTempDb()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);
        return path;
    }
}

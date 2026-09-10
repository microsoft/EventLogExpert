// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Globalization;

namespace EventLogExpert.UI.Tests.Banner;

[Collection(CultureSensitiveCollection.Name)]
public sealed class BannerContentLocalizerTests
{
    public static TheoryData<ExportBlockReason, string, string> ExportBlockCases { get; } = new()
    {
        {
            ExportBlockReason.Faulted,
            "[[Banner_Export_Blocked_Faulted]]",
            "These events cannot be exported because the view could not be prepared."
        },
        {
            ExportBlockReason.Updating,
            "[[Banner_Export_Blocked_Updating]]",
            "These events are still being prepared. Please try again once they have finished loading."
        },
        { ExportBlockReason.NoEvents, "[[Banner_Export_Blocked_NoEvents]]", "There are no events to export." },
        {
            ExportBlockReason.NoColumns,
            "[[Banner_Export_Blocked_NoColumns]]",
            "There are no visible columns to export."
        },
        {
            ExportBlockReason.AlreadyInProgress,
            "[[Banner_Export_Blocked_InProgress]]",
            "An export is already in progress."
        }
    };

    [Fact]
    public void Preformatted_LivesOutsideRuntimeAssembly()
    {
        Assert.NotSame(typeof(BannerMessage).Assembly, typeof(Preformatted).Assembly);
        Assert.DoesNotContain(
            typeof(BannerMessage).Assembly.GetTypes(),
            type => type.Name == nameof(Preformatted));
    }

    [Fact]
    public void Resolve_DatabaseImportSummary_CoversSixResultCases()
    {
        AssertResolved(
            new DatabaseImportSummary(0, [], []),
            "[[Banner_Db_Import_Success_Title]]",
            "[[Banner_Db_Import_None]]",
            "Import Successful",
            "No databases were imported.");
        AssertResolved(
            new DatabaseImportSummary(1, [], []),
            "[[Banner_Db_Import_Success_Title]]",
            "[[Banner_Db_Import_Success_One]]",
            "Import Successful",
            "1 database has successfully been imported");
        AssertResolved(
            new DatabaseImportSummary(3, [], []),
            "[[Banner_Db_Import_Success_Title]]",
            "[[Banner_Db_Import_Success_Many(3)]]",
            "Import Successful",
            "3 databases have successfully been imported");
        AssertResolved(
            new DatabaseImportSummary(0, [new ImportFailure("A.db", new DatabaseFailureReason.NativeDetail("bad"))], []),
            "[[Banner_Db_Import_Failed_Title]]",
            "[[Banner_Db_Import_Failed_Message([[Banner_Db_Import_FailureSummary([[Banner_Db_Import_FailurePart(A.db|bad)]])]])]]",
            "Import Failed",
            "No databases were imported; failed: A.db (bad)");
        AssertResolved(
            new DatabaseImportSummary(1, [new ImportFailure("A.db", new DatabaseFailureReason.NativeDetail("bad"))], []),
            "[[Banner_Db_Import_Partial_Title]]",
            "[[Banner_Db_Import_Partial_Message([[Banner_Db_Import_Partial_One]]|[[Banner_Db_Import_FailureSummary([[Banner_Db_Import_FailurePart(A.db|bad)]])]])]]",
            "Import Completed with Errors",
            "1 database imported; failed: A.db (bad)");
        AssertResolved(
            new DatabaseImportSummary(2, [], [new ImportFailure("B.db", new DatabaseFailureReason.NativeDetail("schema"))]),
            "[[Banner_Db_Import_Partial_Title]]",
            "[[Banner_Db_Import_Partial_Message([[Banner_Db_Import_Partial_Many(2)]]|[[Banner_Db_Import_FailureSummary([[Banner_Db_Import_UpgradeFailurePart(B.db|schema)]])]])]]",
            "Import Completed with Errors",
            "2 databases imported; failed: B.db upgrade (schema)");
    }

    [Fact]
    public void Resolve_DatabaseImportSummary_JoinsMultipleFailuresInSourceOrder()
    {
        AssertResolved(
            new DatabaseImportSummary(
                2,
                [new ImportFailure("A.db", new DatabaseFailureReason.NativeDetail("bad")), new ImportFailure("C.db", new DatabaseFailureReason.NativeDetail("io"))],
                [new ImportFailure("B.db", new DatabaseFailureReason.NativeDetail("schema"))]),
            "[[Banner_Db_Import_Partial_Title]]",
            "[[Banner_Db_Import_Partial_Message([[Banner_Db_Import_Partial_Many(2)]]|[[Banner_Db_Import_FailureSummary([[Banner_Db_Import_FailurePart(A.db|bad)]][[Banner_List_Separator]][[Banner_Db_Import_FailurePart(C.db|io)]][[Banner_List_Separator]][[Banner_Db_Import_UpgradeFailurePart(B.db|schema)]])]])]]",
            "Import Completed with Errors",
            "2 databases imported; failed: A.db (bad), C.db (io), B.db upgrade (schema)");
    }

    [Fact]
    public async Task Resolve_DatabaseImportSummary_RealMissingFileImportNativeDetail_RendersOpaqueReasonByteIdentical()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"BannerContentLocalizerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            await using var service = CreateDatabaseServiceForBannerTest(testDirectory);
            var missingPath = Path.Combine(testDirectory, "missing.db");

            var result = await service.ImportAsync([missingPath], CancellationToken.None);

            var failure = Assert.Single(result.Failures);
            Assert.Equal("missing.db", failure.FileName);
            var nativeDetail = Assert.IsType<DatabaseFailureReason.NativeDetail>(failure.Reason);

            var banner = BannerContentLocalizer.Resolve(
                BuildLocalizer(),
                new DatabaseImportSummary(result.Imported, result.Failures, result.UpgradeFailures));

            Assert.Equal($"No databases were imported; failed: missing.db ({nativeDetail.Detail})", banner.Message);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_DatabaseOperationFailed_CoversOperationNounVariants()
    {
        AssertResolved(
            new DatabaseOperationFailed(new DatabaseOperation.Toggle("a.db"), "denied"),
            "[[Banner_Db_UpdateFailed_Title]]",
            "[[Banner_Db_OperationFailed_Message([[Banner_Db_OperationNoun_Toggle(a.db)]]|denied)]]",
            "Failed to Update Database",
            "An exception occurred while updating 'a.db': denied");
        AssertResolved(
            new DatabaseOperationFailed(new DatabaseOperation.Import(), "zip corrupt"),
            "[[Banner_Db_Import_Failed_Title]]",
            "[[Banner_Db_OperationFailed_Message([[Banner_Db_OperationNoun_Import]]|zip corrupt)]]",
            "Import Failed",
            "An exception occurred while importing provider databases: zip corrupt");
        AssertResolved(
            new DatabaseOperationFailed(new DatabaseOperation.UpgradeSingle("b.db"), "schema"),
            "[[Banner_Db_UpgradeFailed_Title]]",
            "[[Banner_Db_OperationFailed_Message([[Banner_Db_OperationNoun_UpgradeSingle(b.db)]]|schema)]]",
            "Database Upgrade Failed",
            "An exception occurred while upgrading 'b.db': schema");
        AssertResolved(
            new DatabaseOperationFailed(new DatabaseOperation.UpgradeBatch(1), "locked"),
            "[[Banner_Db_UpgradeFailed_Title]]",
            "[[Banner_Db_OperationFailed_Message([[Banner_Db_OperationNoun_UpgradeBatch_One(1)]]|locked)]]",
            "Database Upgrade Failed",
            "An exception occurred while upgrading 1 database: locked");
        AssertResolved(
            new DatabaseOperationFailed(new DatabaseOperation.UpgradeBatch(2), "locked"),
            "[[Banner_Db_UpgradeFailed_Title]]",
            "[[Banner_Db_OperationFailed_Message([[Banner_Db_OperationNoun_UpgradeBatch_Many(2)]]|locked)]]",
            "Database Upgrade Failed",
            "An exception occurred while upgrading 2 databases: locked");
    }

    [Fact]
    public void Resolve_DatabaseSpecificFailures_SelectExpectedOutput()
    {
        AssertResolved(
            new DatabaseRemoveFailed("a.db", "denied"),
            "[[Banner_Db_RemoveFailed_Title]]",
            "[[Banner_Db_RemoveFailed_Message(a.db|denied)]]",
            "Failed to Remove Database",
            "An exception occurred while removing 'a.db': denied");
        AssertResolved(
            new DatabaseUpgradeFailed("b.db", new DatabaseFailureReason.NativeDetail("schema")),
            "[[Banner_Db_UpgradeFailed_Title]]",
            "[[Banner_Db_UpgradeFailed_Message(b.db|schema)]]",
            "Database Upgrade Failed",
            "Failed to upgrade 'b.db': schema");
    }

    [Fact]
    public async Task Resolve_DatabaseUpgradeFailed_RealServiceProviderFailureNativeDetail_RendersOpaqueReasonByteIdentical()
    {
        const string fileName = "provider-fails.db";
        const string providerReason = "provider upgrade detail";
        var testDirectory = Path.Combine(Path.GetTempPath(), $"BannerContentLocalizerTests_{Guid.NewGuid():N}");
        var databaseDirectory = Path.Combine(testDirectory, "Databases");
        Directory.CreateDirectory(databaseDirectory);
        File.WriteAllText(Path.Combine(databaseDirectory, fileName), "seed");

        try
        {
            await using var service = CreateDatabaseServiceForBannerTest(
                testDirectory,
                new ProviderUpgradeFailureMaintenance(providerReason));

            var result = await service.UpgradeBatchAsync(
                [fileName],
                UpgradeProgressScope.Background,
                CancellationToken.None);

            var failure = Assert.Single(result.Failed);
            Assert.Equal(fileName, failure.FileName);
            var nativeDetail = Assert.IsType<DatabaseFailureReason.NativeDetail>(failure.Reason);
            Assert.Equal(providerReason, nativeDetail.Detail);

            var banner = BannerContentLocalizer.Resolve(
                BuildLocalizer(),
                new DatabaseUpgradeFailed(failure.FileName, failure.Reason));

            Assert.Equal($"Failed to upgrade '{fileName}': {nativeDetail.Detail}", banner.Message);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(ExportBlockCases))]
    public void Resolve_ExportBlocked_SelectsExpectedKeyAndOutput(
        ExportBlockReason reason,
        string expectedMarkerMessage,
        string expectedMessage)
    {
        BannerContentText marker = ResolveWithMarker(new ExportBlocked(reason));

        Assert.Equal("[[Banner_Export_Title]]", marker.Title);
        Assert.Equal(expectedMarkerMessage, marker.Message);

        BannerContentText actual = ResolveWithEnUs(new ExportBlocked(reason));

        Assert.Equal("Export events", actual.Title);
        Assert.Equal(expectedMessage, actual.Message);
    }

    [Fact]
    public void Resolve_ExportCanceledAndFailed_SelectExpectedOutput()
    {
        BannerContentText canceled = ResolveWithEnUs(new ExportCanceled());
        BannerContentText failed = ResolveWithEnUs(new ExportFailed("disk full"));

        Assert.Equal("Export canceled", canceled.Title);
        Assert.Equal("The export was canceled.", canceled.Message);
        Assert.Equal("Export failed", failed.Title);
        Assert.Equal("disk full", failed.Message);
    }

    [Fact]
    public void Resolve_ExportComplete_FormatsOneAndManyWithGroupedCount()
    {
        BannerContentText oneMarker = ResolveWithMarker(new ExportComplete(1, @"C:\events.csv"));
        BannerContentText manyMarker = ResolveWithMarker(new ExportComplete(1000, @"C:\events.csv"));

        Assert.Equal("[[Banner_Export_Complete_One(1|C:\\events.csv)]]", oneMarker.Message);
        Assert.Equal("[[Banner_Export_Complete_Many(1000|C:\\events.csv)]]", manyMarker.Message);

        BannerContentText one = ResolveWithEnUs(new ExportComplete(1, @"C:\events.csv"));
        BannerContentText many = ResolveWithEnUs(new ExportComplete(1000, @"C:\events.csv"));

        Assert.Equal("Export complete", one.Title);
        Assert.Equal("Exported 1 event to C:\\events.csv.", one.Message);
        Assert.Equal("Export complete", many.Title);
        Assert.Equal("Exported 1,000 events to C:\\events.csv.", many.Message);
    }

    [Fact]
    public void Resolve_FilterAndEmptyLogs_SelectExpectedOutput()
    {
        AssertResolved(
            new FilterLibraryNotFullyLoaded(1),
            "[[Banner_Filter_NotLoaded_Title]]",
            "[[Banner_Filter_NotLoaded_Message_One]]",
            "Filter library not fully loaded",
            "1 library entry couldn't be read and was left in place to avoid data loss. This usually means the library was written by a newer version of the app.");
        AssertResolved(
            new FilterLibraryNotFullyLoaded(3),
            "[[Banner_Filter_NotLoaded_Title]]",
            "[[Banner_Filter_NotLoaded_Message_Many(3)]]",
            "Filter library not fully loaded",
            "3 library entries couldn't be read and were left in place to avoid data loss. This usually means the library was written by a newer version of the app.");
        AssertResolved(
            new FilterLibraryImportFailed(),
            "[[Banner_Filter_ImportFailed_Title]]",
            "[[Banner_Filter_ImportFailed_Message]]",
            "Couldn't finish importing",
            "Some items couldn't be saved to the filter library. The library was reloaded.");
        AssertResolved(
            new FilterAddToSetFailed(),
            "[[Banner_Filter_AddToSetFailed_Title]]",
            "[[Banner_Filter_AddToSetFailed_Message]]",
            "Couldn't add filter",
            "The filter couldn't be added to the filter set.");
        AssertResolved(
            new FilterSetCreateFailed("My Set"),
            "[[Banner_Filter_CreateFailed_Title]]",
            "[[Banner_Filter_CreateFailed_Message(My Set)]]",
            "Couldn't create filter set",
            "'My Set' couldn't be saved to the filter library.");
        AssertResolved(
            new FilterSetSaveFailed("My Set"),
            "[[Banner_Filter_SaveFailed_Title]]",
            "[[Banner_Filter_SaveFailed_Message(My Set)]]",
            "Couldn't save filter set",
            "'My Set' couldn't be saved to the filter library.");
        AssertResolved(
            new FilterSetUpdateFailed(),
            "[[Banner_Filter_SetUpdateFailed_Title]]",
            "[[Banner_Filter_SetUpdateFailed_Message]]",
            "Couldn't save filter set",
            "Your changes couldn't be saved to the filter library.");
        AssertResolved(
            new LibraryEntryDeleteFailed(),
            "[[Banner_Filter_DeleteFailed_Title]]",
            "[[Banner_Filter_DeleteFailed_Message]]",
            "Couldn't delete",
            "The item couldn't be removed from the filter library.");
        AssertResolved(
            new LibraryEntryFavoriteFailed(),
            "[[Banner_Filter_FavoriteFailed_Title]]",
            "[[Banner_Filter_FavoriteFailed_Message]]",
            "Couldn't update favorite",
            "The favorite change couldn't be saved to the filter library.");
        AssertResolved(
            new LibraryEntryPromoteFailed(),
            "[[Banner_Filter_PromoteFailed_Title]]",
            "[[Banner_Filter_PromoteFailed_Message]]",
            "Couldn't save",
            "This filter couldn't be saved to the library.");
        AssertResolved(
            new LibraryEntryRenameFailed(),
            "[[Banner_Filter_RenameFailed_Title]]",
            "[[Banner_Filter_RenameFailed_Message]]",
            "Couldn't rename",
            "The new name couldn't be saved to the filter library.");
        AssertResolved(
            new LibraryEntrySaveFailed("Entry"),
            "[[Banner_Filter_EntrySaveFailed_Title]]",
            "[[Banner_Filter_EntrySaveFailed_Message(Entry)]]",
            "Couldn't save to library",
            "'Entry' couldn't be saved to the filter library.");
        AssertResolved(
            new LibraryEntryTagsSaveFailed(),
            "[[Banner_Filter_EntryTagsFailed_Title]]",
            "[[Banner_Filter_EntryTagsFailed_Message]]",
            "Couldn't update tags",
            "The tag change couldn't be saved to the filter library.");
        AssertResolved(
            new LibraryEntryUpdateFailed("Entry"),
            "[[Banner_Filter_EntryUpdateFailed_Title]]",
            "[[Banner_Filter_EntryUpdateFailed_Message(Entry)]]",
            "Couldn't save changes",
            "Your changes to 'Entry' couldn't be saved to the filter library.");
        AssertResolved(
            new LibraryTagsBulkUpdateFailed(),
            "[[Banner_Filter_TagBulkFailed_Title]]",
            "[[Banner_Filter_TagBulkFailed_Message]]",
            "Couldn't update tags",
            "The tag change couldn't be saved. The library was reloaded.");
        AssertResolved(
            new EmptyLogs(["Application.evtx"]),
            "[[Banner_EmptyLog_Title]]",
            "[[Banner_EmptyLog_One(Application.evtx)]]",
            "Empty log",
            "Log contains no events: Application.evtx");
        AssertResolved(
            new EmptyLogs(["A.evtx", "B.evtx"]),
            "[[Banner_EmptyLog_Title]]",
            "[[Banner_EmptyLog_Many(2|A.evtx[[Banner_List_Separator]]B.evtx)]]",
            "Empty log",
            "2 logs contained no events: A.evtx, B.evtx");
    }

    [Fact]
    public void Resolve_Preformatted_ReturnsVerbatimTextAndActionLabel()
    {
        BannerContentText withoutAction = ResolveWithMarker(new Preformatted("Title", "Message", "   "));
        BannerContentText withAction = ResolveWithMarker(new Preformatted("Title", "Message", "Resolve"));

        Assert.Equal(new BannerContentText("Title", "Message"), withoutAction);
        Assert.Equal(new BannerContentText("Title", "Message", "Resolve"), withAction);
        Assert.False(new Preformatted("Title", "Message", "   ").RequiresAction);
        Assert.True(new Preformatted("Title", "Message", "Resolve").RequiresAction);
    }

    [Fact]
    public void Resolve_SampleRegistry_CoversEveryBannerMessageLeaf()
    {
        Dictionary<Type, BannerMessage> samples = new()
        {
            [typeof(ExportBlocked)] = new ExportBlocked(ExportBlockReason.NoEvents),
            [typeof(ExportCanceled)] = new ExportCanceled(),
            [typeof(ExportFailed)] = new ExportFailed("failure"),
            [typeof(ExportComplete)] = new ExportComplete(2, @"C:\events.csv"),
            [typeof(DatabaseRemoveFailed)] = new DatabaseRemoveFailed("a.db", "denied"),
            [typeof(DatabaseUpgradeFailed)] = new DatabaseUpgradeFailed("a.db", new DatabaseFailureReason.NativeDetail("schema")),
            [typeof(DatabaseOperationFailed)] = new DatabaseOperationFailed(new DatabaseOperation.Import(), "failure"),
            [typeof(DatabaseImportSummary)] = new DatabaseImportSummary(1, [], []),
            [typeof(FilterLibraryNotFullyLoaded)] = new FilterLibraryNotFullyLoaded(1),
            [typeof(FilterLibraryImportFailed)] = new FilterLibraryImportFailed(),
            [typeof(FilterAddToSetFailed)] = new FilterAddToSetFailed(),
            [typeof(FilterSetCreateFailed)] = new FilterSetCreateFailed("Set"),
            [typeof(FilterSetSaveFailed)] = new FilterSetSaveFailed("Set"),
            [typeof(FilterSetUpdateFailed)] = new FilterSetUpdateFailed(),
            [typeof(LibraryEntryDeleteFailed)] = new LibraryEntryDeleteFailed(),
            [typeof(LibraryEntryFavoriteFailed)] = new LibraryEntryFavoriteFailed(),
            [typeof(LibraryEntryPromoteFailed)] = new LibraryEntryPromoteFailed(),
            [typeof(LibraryEntryRenameFailed)] = new LibraryEntryRenameFailed(),
            [typeof(LibraryEntrySaveFailed)] = new LibraryEntrySaveFailed("Entry"),
            [typeof(LibraryEntryTagsSaveFailed)] = new LibraryEntryTagsSaveFailed(),
            [typeof(LibraryEntryUpdateFailed)] = new LibraryEntryUpdateFailed("Entry"),
            [typeof(LibraryTagsBulkUpdateFailed)] = new LibraryTagsBulkUpdateFailed(),
            [typeof(EmptyLogs)] = new EmptyLogs(["Application.evtx"]),
            [typeof(Preformatted)] = new Preformatted("Title", "Message")
        };
        BannerMessage actionBearingPreformatted = new Preformatted("Title", "Message", "Resolve");

        Type[] actualTypes = [.. typeof(BannerMessage).Assembly.GetTypes()
            .Concat(typeof(Preformatted).Assembly.GetTypes())
            .Where(type => type is { IsAbstract: false } && typeof(BannerMessage).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

        Assert.Equal(
            actualTypes,
            samples.Keys.OrderBy(type => type.FullName, StringComparer.Ordinal));

        IStringLocalizer<SharedResource> localizer = new MarkerLocalizer();
        foreach (BannerMessage sample in samples.Values)
        {
            BannerContentText resolved = BannerContentLocalizer.Resolve(localizer, sample);
            Assert.Equal(sample.RequiresAction, resolved.ActionLabel is not null);
        }

        BannerContentText resolvedAction = BannerContentLocalizer.Resolve(localizer, actionBearingPreformatted);
        Assert.Equal(actionBearingPreformatted.RequiresAction, resolvedAction.ActionLabel is not null);
    }

    private static void AssertResolved(
        BannerMessage content,
        string markerTitle,
        string markerMessage,
        string title,
        string message)
    {
        BannerContentText marker = ResolveWithMarker(content);
        BannerContentText actual = ResolveWithEnUs(content);

        Assert.Equal(markerTitle, marker.Title);
        Assert.Equal(markerMessage, marker.Message);
        Assert.Equal(title, actual.Title);
        Assert.Equal(message, actual.Message);
    }

    private static IStringLocalizer<SharedResource> BuildLocalizer() =>
        new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddEventLogLocalization()
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();

    private static DatabaseService CreateDatabaseServiceForBannerTest(
        string testDirectory,
        IProviderDatabaseMaintenance? maintenanceOverride = null)
    {
        var fileLocationOptions = new FileLocationOptions(testDirectory);
        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);
        var logger = Substitute.For<ITraceLogger>();
        var maintenance = maintenanceOverride ?? Substitute.For<IProviderDatabaseMaintenance>();
        var registry = new DatabaseRegistry(fileLocationOptions, preferences, logger);
        registry.Refresh();
        var classification = new DatabaseClassificationService(registry, fileLocationOptions, maintenance, logger);
        var upgrade = new DatabaseUpgradeService(registry, classification.InitialClassificationTask, maintenance, logger);
        var import = new DatabaseImportService(registry, classification, upgrade, fileLocationOptions, logger);
        var recovery = new DatabaseRecoveryService(registry, classification, fileLocationOptions, maintenance, logger);

        return new DatabaseService(registry, classification, upgrade, import, recovery);
    }

    private static BannerContentText ResolveWithEnUs(BannerMessage content)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            return BannerContentLocalizer.Resolve(BuildLocalizer(), content);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    private static BannerContentText ResolveWithMarker(BannerMessage content) =>
        BannerContentLocalizer.Resolve(new MarkerLocalizer(), content);

    private sealed class ProviderUpgradeFailureMaintenance(string reason) : IProviderDatabaseMaintenance
    {
        public DatabaseSchemaState CheckSchemaState(string databasePath, bool readOnly = false) =>
            new(3);

        public void PerformUpgrade(string databasePath) =>
            throw new DatabaseUpgradeException(databasePath, reason);

        public void PrepareForFileDeletion() { }

        public IReadOnlyList<ProviderDatabaseOsStamp> ReadDistinctSourceOsStamps(string databasePath, int limit) =>
            [];

        public void WalCheckpoint(string databasePath) { }
    }
}

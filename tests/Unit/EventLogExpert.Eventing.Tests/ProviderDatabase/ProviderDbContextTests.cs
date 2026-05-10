// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Tests.TestUtils;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Text;

namespace EventLogExpert.Eventing.Tests.ProviderDatabase;

public sealed class ProviderDbContextTests : IDisposable
{
    private readonly List<string> _tempDatabases = [];

    [Fact]
    public void Constructor_ShouldCreateNewDatabase()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        // Act
        using var context = new ProviderDbContext(dbPath, false);

        // Assert
        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public void Constructor_WithEnsureCreatedFalse_DoesNotCreateDatabase()
    {
        // Arrange — read-only inspection should not auto-create the schema. We use ReadWriteCreate
        // (readOnly: false) so the SQLite file is opened (and a header may be written), then assert
        // directly on `sqlite_master` that no table was created. Asserting on file size here would
        // be brittle: SQLite may write a header even without DDL, and a 0-byte file passes only by
        // coincidence of ReadOnly mode behavior on an empty file.
        var dbPath = CreateTempDatabasePath();

        // Act
        using (var context = new ProviderDbContext(dbPath, readOnly: false, ensureCreated: false))
        {
            // Touching the connection forces SQLite to open the file so the assertion below
            // exercises the same on-disk state callers would observe.
            context.Database.OpenConnection();
            context.Database.CloseConnection();
        }

        // Assert — the ProviderDetails table was NOT created. This is the contract callers care
        // about (a fresh, never-upgraded file should not be auto-populated with the V4 schema).
        using var verifyConnection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        verifyConnection.Open();
        using var verifyCmd = verifyConnection.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM \"sqlite_master\" WHERE \"type\" = 'table' AND \"name\" = 'ProviderDetails'";
        var tableCount = Convert.ToInt32(verifyCmd.ExecuteScalar());
        Assert.Equal(0, tableCount);
    }

    [Fact]
    public void Constructor_WithLogger_ShouldLogInstantiation()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var context = new ProviderDbContext(dbPath, false, logger);

        // Assert
        logger.Received(1).Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Instantiating ProviderDbContext")));
    }

    [Fact]
    public void Constructor_WithReadOnly_ShouldOpenDatabaseInReadOnlyMode()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.SaveChanges();
        }

        // Act
        using var readOnlyContext = new ProviderDbContext(dbPath, true);

        // Assert
        Assert.NotNull(readOnlyContext.ProviderDetails);

        var exception = Record.Exception(() =>
            readOnlyContext.ProviderDetails.Add(EventUtils.CreateProvider("Test")));

        Assert.Null(exception);

        exception = Record.Exception(() => readOnlyContext.SaveChanges());
        Assert.NotNull(exception);
    }

    [Fact]
    public void Database_ShouldSupportConcurrentReads()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        using (var context = new ProviderDbContext(dbPath, false))
        {
            for (int i = 0; i < 10; i++)
            {
                context.ProviderDetails.Add(EventUtils.CreateProvider($"Provider{i}"));
            }

            context.SaveChanges();
        }

        var exceptions = new Exception?[10];
        var results = new int[10];

        // Act
        Parallel.For(0, 10, i =>
            {
                try
                {
                    using var context = new ProviderDbContext(dbPath, true);
                    results[i] = context.ProviderDetails.Count();
                }
                catch (Exception ex)
                {
                    exceptions[i] = ex;
                }
            });

        // Assert
        Assert.All(exceptions, ex => Assert.Null(ex));
        Assert.All(results, count => Assert.Equal(10, count));
    }

    [Fact]
    public void Detection_FreshDatabase_ReportsV4()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        using var context = new ProviderDbContext(dbPath, false);

        // Act
        var state = context.IsUpgradeNeeded();

        // Assert
        Assert.Equal(4, state.CurrentVersion);
        Assert.False(state.NeedsUpgrade);
    }

    [Fact]
    public void Detection_V3Schema_NeedsV4Upgrade()
    {
        // Arrange — V3 schema has BLOB columns but no ResolvedFromOwningPublisher and BINARY PK.
        var dbPath = CreateTempDatabasePath();
        SeedV3Schema(dbPath);

        // Act
        using var context = new ProviderDbContext(dbPath, false);
        var state = context.IsUpgradeNeeded();

        // Assert
        Assert.Equal(3, state.CurrentVersion);
        Assert.True(state.NeedsUpgrade);
    }

    public void Dispose()
    {
        foreach (var dbPath in _tempDatabases)
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void IsUpgradeNeeded_ShouldLogResult()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();

        using var context = new ProviderDbContext(dbPath, false, logger);

        // Act
        var result = context.IsUpgradeNeeded();

        // Assert
        logger.Received(1).Debug(Arg.Is<DebugLogHandler>(h =>
            h.ToString().Contains(nameof(ProviderDbContext.IsUpgradeNeeded)) &&
            h.ToString().Contains("currentVersion") &&
            h.ToString().Contains("needsUpgrade")));
    }

    [Fact]
    public void IsUpgradeNeeded_WithMixedPayloadColumnTypes_ReportsUnknownSentinel()
    {
        // Arrange — a shape with Parameters BLOB but a non-BLOB Messages column. Without the
        // payload-column uniformity check this would have been misclassified as V3 and the
        // subsequent ReadCompressedRow call would crash on the (byte[]) cast for Messages.
        var dbPath = CreateTempDatabasePath();
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE \"ProviderDetails\" (" +
                "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY, " +
                "\"Messages\" INTEGER NOT NULL, " +
                "\"Parameters\" BLOB NOT NULL, " +
                "\"Events\" BLOB NOT NULL, " +
                "\"Keywords\" BLOB NOT NULL, " +
                "\"Opcodes\" BLOB NOT NULL, " +
                "\"Tasks\" BLOB NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        // Act
        using var context = new ProviderDbContext(dbPath, false);
        var state = context.IsUpgradeNeeded();

        // Assert
        Assert.Equal(ProviderDatabaseSchemaVersion.Unknown, state.CurrentVersion);
        Assert.True(state.NeedsUpgrade);
    }

    [Fact]
    public void IsUpgradeNeeded_WithNewDatabase_ShouldReportCurrentSchemaAndNoUpgrade()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        using var context = new ProviderDbContext(dbPath, false);

        // Act
        var state = context.IsUpgradeNeeded();

        // Assert
        Assert.Equal(ProviderDatabaseSchemaVersion.Current, state.CurrentVersion);
        Assert.False(state.NeedsUpgrade);
    }

    [Fact]
    public void IsUpgradeNeeded_WithUnknownShape_ReportsUnknownSentinel()
    {
        // Arrange — create a ProviderDetails table whose column shape matches none of V1/V2/V3/V4.
        // Messages is BLOB but Parameters is TEXT, which never existed as a real schema and signals
        // either corruption or a foreign / future format.
        var dbPath = CreateTempDatabasePath();
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE \"ProviderDetails\" (" +
                "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY, " +
                "\"Messages\" BLOB NOT NULL, " +
                "\"Parameters\" TEXT NOT NULL, " +
                "\"Events\" BLOB NOT NULL, " +
                "\"Keywords\" BLOB NOT NULL, " +
                "\"Opcodes\" BLOB NOT NULL, " +
                "\"Tasks\" BLOB NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        // Act
        using var context = new ProviderDbContext(dbPath, false);
        var state = context.IsUpgradeNeeded();

        // Assert — Unknown sentinel is reported (NeedsUpgrade is true so callers route through
        // PerformUpgradeIfNeeded, which throws a distinct error rather than misclassifying as v1).
        Assert.Equal(ProviderDatabaseSchemaVersion.Unknown, state.CurrentVersion);
        Assert.True(state.NeedsUpgrade);
    }

    [Fact]
    public void IsUpgradeNeeded_WithV1Schema_ShouldReportV1AndUpgradeNeeded()
    {
        // Arrange — V1 schema had no Parameters column; Messages stored as JSON TEXT.
        var dbPath = CreateTempDatabasePath();
        SeedLegacySchema(dbPath, includeParameters: false, parametersType: null, messagesType: "TEXT");

        // Act
        using var context = new ProviderDbContext(dbPath, false);
        var state = context.IsUpgradeNeeded();

        // Assert
        Assert.Equal(1, state.CurrentVersion);
        Assert.True(state.NeedsUpgrade);
    }

    [Fact]
    public void IsUpgradeNeeded_WithV2Schema_ShouldReportV2AndUpgradeNeeded()
    {
        // Arrange — V2 schema kept TEXT payloads and added Parameters as TEXT.
        var dbPath = CreateTempDatabasePath();
        SeedLegacySchema(dbPath, includeParameters: true, parametersType: "TEXT", messagesType: "TEXT");

        // Act
        using var context = new ProviderDbContext(dbPath, false);
        var state = context.IsUpgradeNeeded();

        // Assert
        Assert.Equal(2, state.CurrentVersion);
        Assert.True(state.NeedsUpgrade);
    }

    [Fact]
    public void Name_ShouldNotIncludeFileExtension()
    {
        // Arrange
        var dbPath = Path.Combine(Path.GetTempPath(), "Database.With.Multiple.Dots.db");

        _tempDatabases.Add(dbPath);

        // Act
        using var context = new ProviderDbContext(dbPath, false);

        // Assert
        Assert.Equal("Database.With.Multiple.Dots", context.Name);
        Assert.DoesNotContain(".db", context.Name);
    }

    [Fact]
    public void PerformUpgradeIfNeeded_WithNewDatabase_ShouldDoNothing()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        using var context = new ProviderDbContext(dbPath, false);
        var initialSize = new FileInfo(dbPath).Length;

        // Act
        context.PerformUpgradeIfNeeded();

        // Assert
        var finalSize = new FileInfo(dbPath).Length;
        Assert.Equal(initialSize, finalSize);
    }

    [Fact]
    public void PerformUpgradeIfNeeded_WithUnknownShape_ThrowsAndPreservesTable()
    {
        // Arrange — same unknown shape as the detection test.
        var dbPath = CreateTempDatabasePath();
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE \"ProviderDetails\" (" +
                "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY, " +
                "\"Messages\" BLOB NOT NULL, " +
                "\"Parameters\" TEXT NOT NULL, " +
                "\"Events\" BLOB NOT NULL, " +
                "\"Keywords\" BLOB NOT NULL, " +
                "\"Opcodes\" BLOB NOT NULL, " +
                "\"Tasks\" BLOB NOT NULL)";
            cmd.ExecuteNonQuery();

            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
                "INSERT INTO \"ProviderDetails\" VALUES ('Unknown-Row', X'00', '[]', X'00', X'00', X'00', X'00')";
            insertCmd.ExecuteNonQuery();
        }

        // Act + Assert — distinct error message, file untouched.
        DatabaseUpgradeException? thrown;
        using (var context = new ProviderDbContext(dbPath, false))
        {
            thrown = Assert.Throws<DatabaseUpgradeException>(() => context.PerformUpgradeIfNeeded());
        }

        Assert.Contains("unrecognized schema", thrown.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(dbPath, thrown.DatabasePath);

        AssertProviderDetailsRowCount(dbPath, expectedRows: 1);
    }

    [Fact]
    public void PerformUpgradeIfNeeded_WithV1Schema_ThrowsAndPreservesLegacyTable()
    {
        // Arrange — V1 row has no Parameters column; payloads are JSON TEXT.
        var dbPath = CreateTempDatabasePath();
        SeedLegacySchema(dbPath, includeParameters: false, parametersType: null, messagesType: "TEXT");
        InsertLegacyRow(
            dbPath,
            providerName: "V1Provider",
            messagesJson: "[{\"ShortId\":1,\"LogLink\":null,\"RawId\":1,\"Tag\":null,\"Template\":null,\"Text\":\"hello\"}]",
            parametersJson: null,
            eventsJson: "[]",
            keywordsJson: "{}",
            opcodesJson: "{}",
            tasksJson: "{}");

        // Act + Assert — V1 is no longer auto-upgradable; the upgrade fails fast.
        DatabaseUpgradeException? thrown;
        using (var context = new ProviderDbContext(dbPath, false))
        {
            thrown = Assert.Throws<DatabaseUpgradeException>(() => context.PerformUpgradeIfNeeded());
        }

        Assert.Contains("v1", thrown.Reason);
        Assert.Contains("no longer supported", thrown.Reason);
        Assert.Equal(dbPath, thrown.DatabasePath);

        // The original V1 table is preserved (throw fires before DROP); detection still reports v1.
        using var verify = new ProviderDbContext(dbPath, true);
        var stateAfter = verify.IsUpgradeNeeded();
        Assert.Equal(1, stateAfter.CurrentVersion);
        Assert.True(stateAfter.NeedsUpgrade);
        AssertProviderDetailsRowCount(dbPath, expectedRows: 1);
    }

    [Fact]
    public void PerformUpgradeIfNeeded_WithV2SchemaAndNullParameters_Throws()
    {
        // Arrange — V2 row with NULL Parameters column; previously this would have silently
        // round-tripped to an empty list. Now it must surface the same hard-fail as any other V2 row.
        var dbPath = CreateTempDatabasePath();
        SeedLegacySchema(dbPath, includeParameters: true, parametersType: "TEXT", messagesType: "TEXT");
        InsertLegacyRow(
            dbPath,
            providerName: "V2NullParams",
            messagesJson: "[]",
            parametersJson: null,
            eventsJson: "[]",
            keywordsJson: "{}",
            opcodesJson: "{}",
            tasksJson: "{}");

        // Act + Assert
        using var context = new ProviderDbContext(dbPath, false);
        var thrown = Assert.Throws<DatabaseUpgradeException>(() => context.PerformUpgradeIfNeeded());
        Assert.Contains("v2", thrown.Reason);

        AssertProviderDetailsRowCount(dbPath, expectedRows: 1);
    }

    [Fact]
    public void PerformUpgradeIfNeeded_WithV2Schema_Throws()
    {
        // Arrange — V2 row stores Parameters as JSON TEXT.
        var dbPath = CreateTempDatabasePath();
        SeedLegacySchema(dbPath, includeParameters: true, parametersType: "TEXT", messagesType: "TEXT");
        InsertLegacyRow(
            dbPath,
            providerName: "V2Provider",
            messagesJson: "[]",
            parametersJson: "[{\"ShortId\":2,\"LogLink\":null,\"RawId\":2,\"Tag\":null,\"Template\":null,\"Text\":\"param-text\"}]",
            eventsJson: "[]",
            keywordsJson: "{}",
            opcodesJson: "{}",
            tasksJson: "{}");

        // Act + Assert
        DatabaseUpgradeException? thrown;
        using (var context = new ProviderDbContext(dbPath, false))
        {
            thrown = Assert.Throws<DatabaseUpgradeException>(() => context.PerformUpgradeIfNeeded());
        }

        Assert.Contains("v2", thrown.Reason);
        Assert.Contains("no longer supported", thrown.Reason);

        // Original V2 row preserved; detection still reports v2.
        using var verify = new ProviderDbContext(dbPath, true);
        var stateAfter = verify.IsUpgradeNeeded();
        Assert.Equal(2, stateAfter.CurrentVersion);
        AssertProviderDetailsRowCount(dbPath, expectedRows: 1);
    }

    [Fact]
    public void ProviderDetails_Delete_ShouldRemoveRecord()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var providerName = "DeletableProvider";

        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(EventUtils.CreateProvider(providerName));

            context.SaveChanges();
        }

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            var provider = context.ProviderDetails.First(p => p.ProviderName == providerName);
            context.ProviderDetails.Remove(provider);
            context.SaveChanges();
        }

        // Assert
        using (var context = new ProviderDbContext(dbPath, true))
        {
            Assert.Empty(context.ProviderDetails.Where(p => p.ProviderName == providerName));
        }
    }

    [Fact]
    public void ProviderDetails_ShouldBeAccessible()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        // Act
        using var context = new ProviderDbContext(dbPath, false);

        // Assert
        Assert.NotNull(context.ProviderDetails);
        Assert.Empty(context.ProviderDetails);
    }

    [Fact]
    public void ProviderDetails_ShouldCompressLargeData()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var largeMessages = Enumerable.Range(1, 100)
            .Select(i => EventUtils.CreateMessageModel("LargeProvider", i, new string('A', 1000)))
            .ToList();

        var provider = EventUtils.CreateProvider("LargeProvider", messages: largeMessages);

        // Calculate uncompressed data size: 100 messages * 1000 chars = ~100KB of text data
        const int UncompressedDataSize = 100 * 1000;

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        var fileSize = new FileInfo(dbPath).Length;

        // Compression should result in file size significantly smaller than uncompressed data
        // Allow for SQLite overhead but verify compression achieved at least 2x reduction
        Assert.True(fileSize < UncompressedDataSize / 2,
            $"Expected compressed file size to be less than {UncompressedDataSize / 2} bytes, but was {fileSize} bytes. " +
            $"This suggests compression may not be working effectively.");

        using (var context = new ProviderDbContext(dbPath, true))
        {
            var retrieved = context.ProviderDetails.FirstOrDefault(p => p.ProviderName == "LargeProvider");
            Assert.NotNull(retrieved);
            Assert.Equal(100, retrieved.Messages.Count());
        }
    }

    [Fact]
    public void ProviderDetails_Update_ShouldModifyExistingRecord()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var providerName = "UpdateableProvider";

        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(EventUtils.CreateProvider(
                providerName,
                messages: [EventUtils.CreateMessageModel(providerName, 1, "Original")]));

            context.SaveChanges();
        }

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            var provider = context.ProviderDetails.First(p => p.ProviderName == providerName);
            provider.Messages = [EventUtils.CreateMessageModel(providerName, 1, "Updated")];
            context.SaveChanges();
        }

        // Assert
        using (var context = new ProviderDbContext(dbPath, true))
        {
            var provider = context.ProviderDetails.First(p => p.ProviderName == providerName);
            Assert.Equal("Updated", provider.Messages.First().Text);
        }
    }

    [Fact]
    public void ProviderDetails_WithComplexData_ShouldPreserveAllFields()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var provider = new ProviderDetails
        {
            ProviderName = "ComplexProvider",
            Messages =
            [
                EventUtils.CreateMessageModel("ComplexProvider", 1, "Message1"),
                EventUtils.CreateMessageModel("ComplexProvider", 2, "Message2"),
                EventUtils.CreateMessageModel("ComplexProvider", 3, "Message3")
            ],
            Parameters =
            [
                EventUtils.CreateMessageModel("ComplexProvider", 10, "Param1"),
                EventUtils.CreateMessageModel("ComplexProvider", 11, "Param2")
            ],
            Events =
            [
                EventUtils.CreateEventModel(100, description: "Event1"),
                EventUtils.CreateEventModel(101, description: "Event2")
            ],
            Keywords = new Dictionary<long, string>
            {
                { 1L, "Keyword1" },
                { 2L, "Keyword2" },
                { 4L, "Keyword3" }
            },
            Opcodes = new Dictionary<int, string>
            {
                { 1, "Opcode1" },
                { 2, "Opcode2" }
            },
            Tasks = new Dictionary<int, string>
            {
                { 1, "Task1" },
                { 2, "Task2" },
                { 3, "Task3" }
            }
        };

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        using (var context = new ProviderDbContext(dbPath, true))
        {
            var retrieved = context.ProviderDetails.First(p => p.ProviderName == "ComplexProvider");
            Assert.Equal(3, retrieved.Messages.Count());
            Assert.Equal(2, retrieved.Parameters.Count());
            Assert.Equal(2, retrieved.Events.Count());
            Assert.Equal(3, retrieved.Keywords.Count);
            Assert.Equal(2, retrieved.Opcodes.Count);
            Assert.Equal(3, retrieved.Tasks.Count);
            Assert.Equal("Message1", retrieved.Messages.First().Text);
            Assert.Equal("Param1", retrieved.Parameters.First().Text);
            Assert.Equal("Event1", retrieved.Events.First().Description);
            Assert.Equal("Keyword1", retrieved.Keywords[1L]);
            Assert.Equal("Opcode1", retrieved.Opcodes[1]);
            Assert.Equal("Task1", retrieved.Tasks[1]);
        }
    }

    [Fact]
    public void ProviderDetails_WithEmptyCollections_ShouldPersist()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var provider = EventUtils.CreateProvider("EmptyProvider");

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        using (var context = new ProviderDbContext(dbPath, true))
        {
            var retrieved = context.ProviderDetails.First(p => p.ProviderName == "EmptyProvider");
            Assert.NotNull(retrieved);
            Assert.Empty(retrieved.Messages);
            Assert.Empty(retrieved.Parameters);
            Assert.Empty(retrieved.Events);
            Assert.Empty(retrieved.Keywords);
            Assert.Empty(retrieved.Opcodes);
            Assert.Empty(retrieved.Tasks);
        }
    }

    [Fact]
    public void ProviderDetails_WithMultipleProviders_ShouldRetrieveAll()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var providers = Enumerable.Range(1, 5)
            .Select(i => EventUtils.CreateProvider($"Provider{i}"))
            .ToList();

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            foreach (var provider in providers)
            {
                context.ProviderDetails.Add(provider);
            }

            context.SaveChanges();
        }

        // Assert
        using (var context = new ProviderDbContext(dbPath, true))
        {
            Assert.Equal(5, context.ProviderDetails.Count());
        }
    }

    [Fact]
    public void ProviderDetails_WithSpecialCharacters_ShouldPersist()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var provider = EventUtils.CreateProvider(
            "Special\"Provider'With<>Chars",
            messages: [EventUtils.CreateMessageModel("Special\"Provider'With<>Chars", 1, "Message with \"quotes\" and 'apostrophes'")]);

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        using (var context = new ProviderDbContext(dbPath, true))
        {
            var retrieved = context.ProviderDetails.First(p => p.ProviderName == "Special\"Provider'With<>Chars");
            Assert.NotNull(retrieved);
            Assert.Equal("Message with \"quotes\" and 'apostrophes'", retrieved.Messages.First().Text);
        }
    }

    [Fact]
    public void ProviderName_LookupOnV4Schema_UsesPrimaryKeyIndex()
    {
        // Arrange — populate a V4 database so EXPLAIN QUERY PLAN has rows to plan against.
        var dbPath = CreateTempDatabasePath();

        using (var context = new ProviderDbContext(dbPath, false))
        {
            for (var i = 0; i < 5; i++)
            {
                context.ProviderDetails.Add(EventUtils.CreateProvider($"Provider-{i}"));
            }

            context.SaveChanges();
        }

        // Act — ask SQLite directly how it would resolve a case-insensitive PK lookup.
        // The lookup should use the auto-generated PK index, not a table scan.
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM \"ProviderDetails\" WHERE \"ProviderName\" = 'provider-2'";
        using var reader = cmd.ExecuteReader();

        var planText = new StringBuilder();
        while (reader.Read())
        {
            planText.AppendLine(reader["detail"]?.ToString());
        }

        var plan = planText.ToString();

        // Assert — weak property: the plan mentions an index (covers `USING INDEX` / `USING COVERING INDEX` /
        // `SEARCH ... USING INTEGER PRIMARY KEY` variants without anchoring on exact wording across SQLite versions).
        Assert.True(
            plan.Contains("USING INDEX", StringComparison.OrdinalIgnoreCase) ||
                plan.Contains("USING COVERING INDEX", StringComparison.OrdinalIgnoreCase) ||
                plan.Contains("USING PRIMARY KEY", StringComparison.OrdinalIgnoreCase),
            $"Expected ProviderName lookup to use the PK index, but plan was:\n{plan}");

        // And explicitly: the plan must NOT be a table scan.
        Assert.DoesNotContain("SCAN ", plan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvedFromOwningPublisher_RoundTripsThroughDatabase()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var provider = EventUtils.CreateProvider(
            "ResolvedRoundTrip",
            resolvedFromOwningPublisher: "Owning-Publisher-Name");

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        using var verify = new ProviderDbContext(dbPath, true);
        var retrieved = verify.ProviderDetails.Single(p => p.ProviderName == "ResolvedRoundTrip");
        Assert.Equal("Owning-Publisher-Name", retrieved.ResolvedFromOwningPublisher);
    }

    [Fact]
    public void Schema_V4_PrimaryKey_UsesNoCaseCollation()
    {
        // Arrange — a fresh database is created at the current schema version.
        var dbPath = CreateTempDatabasePath();

        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.SaveChanges();
        }

        // Act
        var collation = ReadProviderNamePrimaryKeyCollation(dbPath);

        // Assert
        Assert.Equal("NOCASE", collation, ignoreCase: true);
    }

    [Fact]
    public void Upgrade_V3_To_V4_HappyPath()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        SeedV3Schema(dbPath);

        var seeded = EventUtils.CreateProvider(
            "V3-Provider",
            messages: [EventUtils.CreateMessageModel("V3-Provider", 1, "from-v3")],
            keywords: new Dictionary<long, string> { { 1L, "kw" } });
        InsertV3Row(dbPath, seeded);

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.PerformUpgradeIfNeeded();
        }

        // Assert
        Assert.Equal("NOCASE", ReadProviderNamePrimaryKeyCollation(dbPath), ignoreCase: true);

        using var verify = new ProviderDbContext(dbPath, true);
        var stateAfter = verify.IsUpgradeNeeded();
        Assert.Equal(ProviderDatabaseSchemaVersion.Current, stateAfter.CurrentVersion);
        Assert.False(stateAfter.NeedsUpgrade);

        var row = verify.ProviderDetails.Single();
        Assert.Equal("V3-Provider", row.ProviderName);
        Assert.Equal("from-v3", row.Messages.Single().Text);
        Assert.Equal("kw", row.Keywords[1L]);
        Assert.Null(row.ResolvedFromOwningPublisher);
    }

    [Fact]
    public void Upgrade_V3_To_V4_MergesCaseCollidingProviders()
    {
        // Arrange — seed two rows whose ProviderName differs only by case. V3 BINARY PK allows this;
        // V4 NOCASE PK does not, so the upgrade must merge them.
        var dbPath = CreateTempDatabasePath();
        SeedV3Schema(dbPath);

        var first = EventUtils.CreateProvider(
            "Microsoft-Foo",
            messages: [EventUtils.CreateMessageModel("Microsoft-Foo", 1, "shared-text")],
            events: [EventUtils.CreateEventModel(100, description: "shared-event")],
            keywords: new Dictionary<long, string> { { 1L, "alpha" } });

        var second = EventUtils.CreateProvider(
            "microsoft-foo",
            messages: [EventUtils.CreateMessageModel("microsoft-foo", 1, "shared-text")],
            events: [EventUtils.CreateEventModel(100, description: "shared-event")],
            keywords: new Dictionary<long, string> { { 2L, "beta" } });

        InsertV3Row(dbPath, first);
        InsertV3Row(dbPath, second);

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.PerformUpgradeIfNeeded();
        }

        // Assert — merged into a single row using the first-encountered casing as canonical.
        using var verify = new ProviderDbContext(dbPath, true);
        var rows = verify.ProviderDetails.ToList();
        Assert.Single(rows);
        Assert.Equal("Microsoft-Foo", rows[0].ProviderName);
        Assert.Single(rows[0].Messages);
        Assert.Single(rows[0].Events);
        Assert.Equal(2, rows[0].Keywords.Count);
        Assert.Equal("alpha", rows[0].Keywords[1L]);
        Assert.Equal("beta", rows[0].Keywords[2L]);
    }

    [Fact]
    public void Upgrade_V3_To_V4_MergesCaseCollidingProvidersWithParameters()
    {
        // Arrange — case-colliding rows with non-empty Parameters that share identity (same
        // ProviderName, RawId, ShortId, Tag). Without the V3 Parameters BLOB fix this scenario
        // was masked because Parameters silently came back empty before the merger ran.
        var dbPath = CreateTempDatabasePath();
        SeedV3Schema(dbPath);

        var first = new ProviderDetails
        {
            ProviderName = "Microsoft-Foo",
            Messages = [],
            Parameters =
            [
                EventUtils.CreateMessageModel("Microsoft-Foo", 1, "shared-param", shortId: 1)
            ],
            Events = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var second = new ProviderDetails
        {
            ProviderName = "microsoft-foo",
            Messages = [],
            Parameters =
            [
                EventUtils.CreateMessageModel("microsoft-foo", 1, "shared-param", shortId: 1),
                EventUtils.CreateMessageModel("microsoft-foo", 2, "second-only", shortId: 2)
            ],
            Events = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        InsertV3Row(dbPath, first);
        InsertV3Row(dbPath, second);

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.PerformUpgradeIfNeeded();
        }

        // Assert — single merged row using first-encountered casing; deduplicated parameters union.
        using var verify = new ProviderDbContext(dbPath, true);
        var rows = verify.ProviderDetails.ToList();
        Assert.Single(rows);
        Assert.Equal("Microsoft-Foo", rows[0].ProviderName);
        var parameters = rows[0].Parameters.ToList();
        Assert.Equal(2, parameters.Count);
        Assert.Contains(parameters, p => p.RawId == 1 && p.Text == "shared-param");
        Assert.Contains(parameters, p => p.RawId == 2 && p.Text == "second-only");
    }

    [Fact]
    public void Upgrade_V3_To_V4_OnConflict_ThrowsAndPreservesOriginalRows()
    {
        // Arrange — case-colliding rows whose Keywords disagree on the same numeric key.
        var dbPath = CreateTempDatabasePath();
        SeedV3Schema(dbPath);

        InsertV3Row(
            dbPath,
            EventUtils.CreateProvider(
                "Conflict-Provider",
                keywords: new Dictionary<long, string> { { 1L, "first" } }));

        InsertV3Row(
            dbPath,
            EventUtils.CreateProvider(
                "conflict-provider",
                keywords: new Dictionary<long, string> { { 1L, "second" } }));

        // Act + Assert — upgrade fails fast and the V3 table is not dropped.
        using (var context = new ProviderDbContext(dbPath, false))
        {
            Assert.Throws<DatabaseUpgradeException>(() => context.PerformUpgradeIfNeeded());
        }

        // Original V3 rows are preserved (table still has 2 rows, schema still V3).
        using var verifyConnection = new SqliteConnection($"Data Source={dbPath}");
        verifyConnection.Open();
        using var verifyCmd = verifyConnection.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM \"ProviderDetails\"";
        var count = Convert.ToInt32(verifyCmd.ExecuteScalar());
        Assert.Equal(2, count);
    }

    [Fact]
    public void Upgrade_V3_To_V4_PreservesNonEmptyParameters()
    {
        // Arrange — V3 row with non-empty Parameters (compressed BLOB). The earlier read path
        // mishandled BLOB Parameters and silently emptied them; this test locks in correct preservation.
        var dbPath = CreateTempDatabasePath();
        SeedV3Schema(dbPath);

        var seeded = new ProviderDetails
        {
            ProviderName = "V3-WithParams",
            Messages = [],
            Parameters =
            [
                EventUtils.CreateMessageModel("V3-WithParams", 100, "param-one", shortId: 100),
                EventUtils.CreateMessageModel("V3-WithParams", 200, "param-two", shortId: 200)
            ],
            Events = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };
        InsertV3Row(dbPath, seeded);

        // Act
        using (var context = new ProviderDbContext(dbPath, false))
        {
            context.PerformUpgradeIfNeeded();
        }

        // Assert — both parameter rows survived the destructive upgrade.
        using var verify = new ProviderDbContext(dbPath, true);
        var row = verify.ProviderDetails.Single(p => p.ProviderName == "V3-WithParams");
        var parameters = row.Parameters.ToList();
        Assert.Equal(2, parameters.Count);
        Assert.Contains(parameters, p => p.RawId == 100 && p.Text == "param-one");
        Assert.Contains(parameters, p => p.RawId == 200 && p.Text == "param-two");
    }

    private static void AssertProviderDetailsRowCount(string dbPath, int expectedRows)
    {
        using var verifyConnection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        verifyConnection.Open();
        using var verifyCmd = verifyConnection.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM \"ProviderDetails\"";
        var count = Convert.ToInt32(verifyCmd.ExecuteScalar());
        Assert.Equal(expectedRows, count);
    }

    private static void DeleteDatabaseFile(string path)
    {
        try
        {
            if (!File.Exists(path)) { return; }

            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    File.Delete(path);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
            }
        }
        catch
        {
            // Cleanup is best effort
        }
    }

    private static void InsertLegacyRow(
        string dbPath,
        string providerName,
        string messagesJson,
        string? parametersJson,
        string eventsJson,
        string keywordsJson,
        string opcodesJson,
        string tasksJson)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        // Detect whether the legacy schema includes Parameters; insert the right column list.
        bool hasParameters;
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(\"ProviderDetails\")";
            using var pr = pragma.ExecuteReader();
            hasParameters = false;
            while (pr.Read())
            {
                if (string.Equals(pr["name"]?.ToString(), "Parameters", StringComparison.Ordinal))
                {
                    hasParameters = true;
                    break;
                }
            }
        }

        using var cmd = connection.CreateCommand();
        if (hasParameters)
        {
            cmd.CommandText = "INSERT INTO \"ProviderDetails\" (\"ProviderName\", \"Messages\", \"Events\", \"Keywords\", \"Opcodes\", \"Tasks\", \"Parameters\") " +
                              "VALUES ($name, $messages, $events, $keywords, $opcodes, $tasks, $parameters)";
            cmd.Parameters.AddWithValue("$parameters", (object?)parametersJson ?? DBNull.Value);
        }
        else
        {
            cmd.CommandText = "INSERT INTO \"ProviderDetails\" (\"ProviderName\", \"Messages\", \"Events\", \"Keywords\", \"Opcodes\", \"Tasks\") " +
                              "VALUES ($name, $messages, $events, $keywords, $opcodes, $tasks)";
        }

        cmd.Parameters.AddWithValue("$name", providerName);
        cmd.Parameters.AddWithValue("$messages", messagesJson);
        cmd.Parameters.AddWithValue("$events", eventsJson);
        cmd.Parameters.AddWithValue("$keywords", keywordsJson);
        cmd.Parameters.AddWithValue("$opcodes", opcodesJson);
        cmd.Parameters.AddWithValue("$tasks", tasksJson);
        cmd.ExecuteNonQuery();
    }

    private static void InsertV3Row(string dbPath, ProviderDetails details)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO \"ProviderDetails\" (\"ProviderName\", \"Messages\", \"Parameters\", \"Events\", \"Keywords\", \"Opcodes\", \"Tasks\") " +
            "VALUES ($name, $messages, $parameters, $events, $keywords, $opcodes, $tasks)";
        cmd.Parameters.AddWithValue("$name", details.ProviderName);
        cmd.Parameters.AddWithValue("$messages", CompressedJsonValueConverter<IReadOnlyList<MessageModel>>.ConvertToCompressedJson(details.Messages));
        cmd.Parameters.AddWithValue("$parameters", CompressedJsonValueConverter<IEnumerable<MessageModel>>.ConvertToCompressedJson(details.Parameters));
        cmd.Parameters.AddWithValue("$events", CompressedJsonValueConverter<IReadOnlyList<EventModel>>.ConvertToCompressedJson(details.Events));
        cmd.Parameters.AddWithValue("$keywords", CompressedJsonValueConverter<IDictionary<long, string>>.ConvertToCompressedJson(details.Keywords));
        cmd.Parameters.AddWithValue("$opcodes", CompressedJsonValueConverter<IDictionary<int, string>>.ConvertToCompressedJson(details.Opcodes));
        cmd.Parameters.AddWithValue("$tasks", CompressedJsonValueConverter<IDictionary<int, string>>.ConvertToCompressedJson(details.Tasks));
        cmd.ExecuteNonQuery();
    }

    private static string ReadProviderNamePrimaryKeyCollation(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        string? pkIndexName = null;

        using (var listCmd = connection.CreateCommand())
        {
            listCmd.CommandText = "PRAGMA index_list(\"ProviderDetails\")";
            using var reader = listCmd.ExecuteReader();

            while (reader.Read())
            {
                var origin = reader["origin"]?.ToString();
                if (string.Equals(origin, "pk", StringComparison.OrdinalIgnoreCase))
                {
                    pkIndexName = reader["name"]?.ToString();
                    break;
                }
            }
        }

        Assert.False(string.IsNullOrEmpty(pkIndexName), "Expected a primary-key auto-index on ProviderDetails.");

        using var infoCmd = connection.CreateCommand();
        infoCmd.CommandText = $"PRAGMA index_xinfo(\"{pkIndexName}\")";
        using var infoReader = infoCmd.ExecuteReader();

        while (infoReader.Read())
        {
            var name = infoReader["name"]?.ToString();
            if (string.Equals(name, nameof(ProviderDetails.ProviderName), StringComparison.Ordinal))
            {
                return infoReader["coll"]?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static void SeedLegacySchema(
        string dbPath,
        bool includeParameters,
        string? parametersType,
        string messagesType)
    {
        // Build a legacy ProviderDetails table directly via raw SQLite, before any
        // ProviderDbContext touches the file. EnsureCreated() will then be a no-op
        // because the table already exists, so the legacy schema reaches IsUpgradeNeeded
        // and PerformUpgradeIfNeeded unmodified.
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        var columns = new List<string>
        {
            "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY",
            $"\"Messages\" {messagesType} NOT NULL",
            $"\"Events\" {messagesType} NOT NULL",
            $"\"Keywords\" {messagesType} NOT NULL",
            $"\"Opcodes\" {messagesType} NOT NULL",
            $"\"Tasks\" {messagesType} NOT NULL"
        };

        if (includeParameters)
        {
            columns.Add($"\"Parameters\" {parametersType}");
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE TABLE \"ProviderDetails\" ({string.Join(", ", columns)})";
        cmd.ExecuteNonQuery();
    }

    private static void SeedV3Schema(string dbPath)
    {
        // V3 schema: BLOB payload columns, Parameters as BLOB, no ResolvedFromOwningPublisher
        // column, default (BINARY) collation on the ProviderName PK. Built by raw SQL so
        // ProviderDbContext.EnsureCreated() sees the table as already-existing and skips
        // its V4 schema generation, exposing the legacy V3 shape to detection and upgrade paths.
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE \"ProviderDetails\" (" +
            "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY, " +
            "\"Messages\" BLOB NOT NULL, " +
            "\"Parameters\" BLOB NOT NULL, " +
            "\"Events\" BLOB NOT NULL, " +
            "\"Keywords\" BLOB NOT NULL, " +
            "\"Opcodes\" BLOB NOT NULL, " +
            "\"Tasks\" BLOB NOT NULL)";
        cmd.ExecuteNonQuery();
    }

    private string CreateTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid()}.db");
        _tempDatabases.Add(path);
        return path;
    }
}

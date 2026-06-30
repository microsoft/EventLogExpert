// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Database;

public sealed class DatabaseImportServiceTests
{
    [Fact]
    public async Task ImportAsync_WhenSourceAlreadyEqualsManagedDestination_CountsImportedWithoutCopyException()
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, "DatabaseImportServiceTests", Guid.NewGuid().ToString("N"));
        var fileLocationOptions = new FileLocationOptions(basePath);
        Directory.CreateDirectory(fileLocationOptions.DatabasePath);
        var sourcePath = Path.Combine(fileLocationOptions.DatabasePath, "self-copy.db");
        await File.WriteAllTextAsync(sourcePath, "not empty", TestContext.Current.CancellationToken);

        var preferences = new CapturingDatabasePreferencesProvider();

        var logger = Substitute.For<ITraceLogger>();
        var maintenance = Substitute.For<IProviderDatabaseMaintenance>();
        maintenance.CheckSchemaState(sourcePath).Returns(new DatabaseSchemaState(DatabaseSchemaVersion.Current));
        maintenance.ReadDistinctSourceOsStamps(sourcePath, Arg.Any<int>()).Returns([]);

        var registry = new DatabaseRegistry(fileLocationOptions, preferences, logger);
        var classification = new DatabaseClassificationService(registry, fileLocationOptions, maintenance, logger);
        await classification.InitialClassificationTask;
        await using var upgrade = new DatabaseUpgradeService(registry, classification.InitialClassificationTask, maintenance, logger);
        var import = new DatabaseImportService(registry, classification, upgrade, fileLocationOptions, logger);

        var result = await import.ImportAsync([sourcePath], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Equal(["self-copy.db"], result.ImportedNames);
        Assert.Empty(result.Failures);
        Assert.Contains("self-copy.db", preferences.DisabledDatabasesPreference);
        Assert.Contains(registry.Entries, entry =>
            entry.FileName == "self-copy.db" &&
            entry is { IsEnabled: false, Status: DatabaseStatus.Ready });

        Directory.Delete(basePath, recursive: true);
    }

    private sealed class CapturingDatabasePreferencesProvider : IDatabasePreferencesProvider
    {
        public IEnumerable<string> DisabledDatabasesPreference { get; set; } = [];
    }
}

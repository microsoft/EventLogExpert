// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.LogTable;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ColumnResetMigratorTests
{
    private const string CompletionKey = "log-table-column-reset-migration-state";

    [Fact]
    public void RunMigration_ClearsNumericListPreferencesAndMarksComplete()
    {
        var preferences = Substitute.For<ILegacyPreferences>();
        var sut = new ColumnResetMigrator(preferences, Substitute.For<ITraceLogger>());

        sut.RunMigration();

        preferences.Received(1).Remove(LogTablePreferenceKeys.EnabledEventTableColumns);
        preferences.Received(1).Remove(LogTablePreferenceKeys.ColumnOrder);
        preferences.Received(1).SetString(CompletionKey, "1");
    }

    [Fact]
    public void RunMigration_DoesNotTouchNameKeyedWidths()
    {
        var preferences = Substitute.For<ILegacyPreferences>();
        var sut = new ColumnResetMigrator(preferences, Substitute.For<ITraceLogger>());

        sut.RunMigration();

        preferences.DidNotReceive().Remove(LogTablePreferenceKeys.ColumnWidths);
    }

    [Fact]
    public void RunMigration_WhenPreferenceStoreThrows_DoesNotThrowAndDoesNotMarkComplete()
    {
        var preferences = Substitute.For<ILegacyPreferences>();
        preferences.When(p => p.Remove(Arg.Any<string>())).Do(_ => throw new InvalidOperationException("store failure"));
        var sut = new ColumnResetMigrator(preferences, Substitute.For<ITraceLogger>());

        var exception = Record.Exception(sut.RunMigration);

        Assert.Null(exception);
        preferences.DidNotReceive().SetString(CompletionKey, Arg.Any<string>());
    }

    [Fact]
    public void ShouldRunMigration_WhenFlagAbsent_ReturnsTrue()
    {
        var preferences = Substitute.For<ILegacyPreferences>();
        preferences.GetString(CompletionKey).Returns((string?)null);
        var sut = new ColumnResetMigrator(preferences, Substitute.For<ITraceLogger>());

        Assert.True(sut.ShouldRunMigration());
    }

    [Fact]
    public void ShouldRunMigration_WhenFlagSet_ReturnsFalse()
    {
        var preferences = Substitute.For<ILegacyPreferences>();
        preferences.GetString(CompletionKey).Returns("1");
        var sut = new ColumnResetMigrator(preferences, Substitute.For<ITraceLogger>());

        Assert.False(sut.ShouldRunMigration());
    }

    [Fact]
    public void ShouldRunMigration_WhenReadThrows_ReturnsFalse()
    {
        var preferences = Substitute.For<ILegacyPreferences>();
        preferences.GetString(CompletionKey).Returns(_ => throw new InvalidOperationException("store failure"));
        var sut = new ColumnResetMigrator(preferences, Substitute.For<ITraceLogger>());

        Assert.False(sut.ShouldRunMigration());
    }
}

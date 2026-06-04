// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class BackslashNameMigratorTests
{
    private const string CompletionKey = "filter-library-backslash-migration-state";

    [Fact]
    public void BuildMigrationPlan_CollisionWithNonBackslashEntry_AppendsSuffix()
    {
        var sut = new BackslashNameMigrator(Substitute.For<ILegacyPreferences>(), Substitute.For<ITraceLogger>());
        var entries = new List<LibraryEntry>
        {
            BuildFilterSet("Existing"),
            BuildFilterSet(@"Network\Existing"),
        };

        var plan = sut.BuildMigrationPlan(entries);

        var migrated = Assert.Single(plan.UpdatedEntries);
        Assert.Equal("Existing (2)", migrated.Name);
        Assert.Equal(["network"], migrated.Tags);
        Assert.Equal(1, plan.CollisionsDisambiguated);
    }

    [Fact]
    public void BuildMigrationPlan_NoBackslashEntries_ReturnsEmptyPlan()
    {
        var sut = new BackslashNameMigrator(Substitute.For<ILegacyPreferences>(), Substitute.For<ITraceLogger>());
        var entries = new List<LibraryEntry>
        {
            BuildFilterSet("Plain"),
            BuildFilterSet("Another"),
        };

        var plan = sut.BuildMigrationPlan(entries);

        Assert.Empty(plan.UpdatedEntries);
        Assert.Equal(0, plan.CollisionsDisambiguated);
    }

    [Fact]
    public void BuildMigrationPlan_NullArg_Throws()
    {
        var sut = new BackslashNameMigrator(Substitute.For<ILegacyPreferences>(), Substitute.For<ITraceLogger>());

        Assert.Throws<ArgumentNullException>(() => sut.BuildMigrationPlan(null!));
    }

    [Fact]
    public void BuildMigrationPlan_RunsAreIdempotent_SecondPassNoOp()
    {
        var sut = new BackslashNameMigrator(Substitute.For<ILegacyPreferences>(), Substitute.For<ITraceLogger>());
        var entries = new List<LibraryEntry> { BuildFilterSet(@"Exchange\HUB") };

        var firstPlan = sut.BuildMigrationPlan(entries);
        var migratedEntries = firstPlan.UpdatedEntries.ToList();
        var secondPlan = sut.BuildMigrationPlan(migratedEntries);

        Assert.Empty(secondPlan.UpdatedEntries);
    }

    [Fact]
    public void BuildMigrationPlan_SingleBackslashEntry_MigratesToFlatNameAndTags()
    {
        var sut = new BackslashNameMigrator(Substitute.For<ILegacyPreferences>(), Substitute.For<ITraceLogger>());
        var entries = new List<LibraryEntry> { BuildFilterSet(@"Exchange\HUB") };

        var plan = sut.BuildMigrationPlan(entries);

        var migrated = Assert.Single(plan.UpdatedEntries);
        Assert.Equal("HUB", migrated.Name);
        Assert.Equal(["exchange"], migrated.Tags);
        Assert.Equal(0, plan.CollisionsDisambiguated);
    }

    [Fact]
    public void BuildMigrationPlan_TwoBackslashEntriesShareLeafName_FirstKeepsNameSecondGetsSuffix()
    {
        var sut = new BackslashNameMigrator(Substitute.For<ILegacyPreferences>(), Substitute.For<ITraceLogger>());
        var entries = new List<LibraryEntry>
        {
            BuildFilterSet(@"A\Child"),
            BuildFilterSet(@"B\Child"),
        };

        var plan = sut.BuildMigrationPlan(entries);

        Assert.Equal(2, plan.UpdatedEntries.Count);
        Assert.Contains(plan.UpdatedEntries, e => e.Name == "Child");
        Assert.Contains(plan.UpdatedEntries, e => e.Name == "Child (2)");
        Assert.Equal(1, plan.CollisionsDisambiguated);
    }

    [Fact]
    public void MarkMigrationCompleted_PersistsCompletionFlag()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        var sut = new BackslashNameMigrator(prefs, Substitute.For<ITraceLogger>());

        sut.MarkMigrationCompleted();

        prefs.Received(1).SetString(CompletionKey, "1");
    }

    [Fact]
    public void ShouldRunMigration_CompletionFlagSet_ReturnsFalse()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        prefs.GetString(CompletionKey).Returns("1");
        var sut = new BackslashNameMigrator(prefs, Substitute.For<ITraceLogger>());

        Assert.False(sut.ShouldRunMigration());
    }

    [Fact]
    public void ShouldRunMigration_NoCompletedFlag_ReturnsTrue()
    {
        var prefs = Substitute.For<ILegacyPreferences>();
        prefs.GetString(CompletionKey).Returns((string?)null);
        var sut = new BackslashNameMigrator(prefs, Substitute.For<ITraceLogger>());

        Assert.True(sut.ShouldRunMigration());
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = ImmutableList<SavedFilter>.Empty,
        };
}

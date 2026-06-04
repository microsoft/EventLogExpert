// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class ImportPreflightCtorTests
{
    [Fact]
    public void Constructor_EmptyListsAndNoError_ProducesEmptyResult()
    {
        var preflight = new ImportPreflight([], [], []);

        Assert.Empty(preflight.ToAdd);
        Assert.Empty(preflight.ToReplace);
        Assert.Empty(preflight.SkippedDuplicates);
        Assert.Null(preflight.Error);
    }

    [Fact]
    public void Constructor_FiveBucketOverload_AcceptsToUpdateAndAmbiguousMatches()
    {
        var existing = BuildFilterSet("Existing");
        var incoming = BuildFilterSet("Incoming");
        var preflight = new ImportPreflight(
            toAdd: [],
            toReplace: [],
            skippedDuplicates: [],
            toUpdate: [(existing, incoming)],
            ambiguousMatches: [([existing], incoming)]);

        Assert.Single(preflight.ToUpdate);
        Assert.Single(preflight.AmbiguousMatches);
    }

    [Fact]
    public void Constructor_NullAmbiguousMatches_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ImportPreflight([], [], [], toUpdate: [], ambiguousMatches: null!));
    }

    [Fact]
    public void Constructor_NullErrorIsAllowed()
    {
        var preflight = new ImportPreflight([], [], [], error: null);

        Assert.Null(preflight.Error);
    }

    [Fact]
    public void Constructor_NullSkippedDuplicates_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ImportPreflight([], [], null!));
    }

    [Fact]
    public void Constructor_NullToAdd_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ImportPreflight(null!, [], []));
    }

    [Fact]
    public void Constructor_NullToReplace_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ImportPreflight([], null!, []));
    }

    [Fact]
    public void Constructor_NullToUpdate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ImportPreflight([], [], [], toUpdate: null!, ambiguousMatches: []));
    }

    [Fact]
    public void Constructor_ThreeBucketOverload_DefaultsToUpdateAndAmbiguousMatchesToEmpty()
    {
        var preflight = new ImportPreflight([], [], []);

        Assert.Empty(preflight.ToUpdate);
        Assert.Empty(preflight.AmbiguousMatches);
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [],
        };
}

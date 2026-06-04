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
}

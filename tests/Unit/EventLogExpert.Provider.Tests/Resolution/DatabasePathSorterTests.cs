// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Provider.Tests.Resolution;

public sealed class CatalogPathSorterTests
{
    [Fact]
    public void Sort_PreservesDirectory_ShouldReturnFullPaths()
    {
        var databases = new[]
        {
            @"C:\Databases\Test\Windows 2019.db",
            @"C:\Databases\Prod\Exchange 2016.db"
        };

        var sorted = DatabasePathSorter.Sort(databases);

        Assert.All(sorted, path => Assert.True(Path.IsPathRooted(path)));
        Assert.Contains(sorted, s => s.Contains(@"Databases\Test"));
        Assert.Contains(sorted, s => s.Contains(@"Databases\Prod"));
    }

    [Fact]
    public void Sort_WithComplexVersionStrings_ShouldHandleCorrectly()
    {
        var databases = new[]
        {
            @"C:\Test\Product v2.0.db",
            @"C:\Test\Product v1.5.db",
            @"C:\Test\Product RC1.db"
        };

        var sorted = DatabasePathSorter.Sort(databases);

        Assert.Equal(3, sorted.Count);
        Assert.Equal(@"C:\Test\Product v2.0.db", sorted[0]);
        Assert.Equal(@"C:\Test\Product v1.5.db", sorted[1]);
        Assert.Equal(@"C:\Test\Product RC1.db", sorted[2]);
    }

    [Fact]
    public void Sort_WithEmptyList_ShouldReturnEmptyList()
    {
        var sorted = DatabasePathSorter.Sort([]);

        Assert.Empty(sorted);
    }

    [Fact]
    public void Sort_WithFileNamesOnly_PreservesNames()
    {
        var fileNames = new[] { "Exchange 2019.db", "Exchange 2016.db", "Windows 2019.db" };

        var sorted = DatabasePathSorter.Sort(fileNames);

        Assert.Equal(3, sorted.Count);
        Assert.Equal("Exchange 2019.db", sorted[0]);
        Assert.Equal("Exchange 2016.db", sorted[1]);
        Assert.Equal("Windows 2019.db", sorted[2]);
    }

    [Fact]
    public void Sort_WithMixedVersionsAndNoVersions_ShouldSortCorrectly()
    {
        var databases = new[]
        {
            @"C:\Test\Windows 2019.db",
            @"C:\Test\Exchange.db",
            @"C:\Test\Windows 2016.db",
            @"C:\Test\Azure 2020.db"
        };

        var sorted = DatabasePathSorter.Sort(databases);

        Assert.Equal(4, sorted.Count);
        Assert.Equal(@"C:\Test\Azure 2020.db", sorted[0]);
        Assert.Equal(@"C:\Test\Exchange.db", sorted[1]);
        Assert.Equal(@"C:\Test\Windows 2019.db", sorted[2]);
        Assert.Equal(@"C:\Test\Windows 2016.db", sorted[3]);
    }

    [Fact]
    public void Sort_WithNoVersion_ShouldSortByProductName()
    {
        var databases = new[]
        {
            @"C:\Test\Windows.db",
            @"C:\Test\Exchange.db",
            @"C:\Test\Azure.db"
        };

        var sorted = DatabasePathSorter.Sort(databases);

        Assert.Equal(3, sorted.Count);
        Assert.Equal(@"C:\Test\Azure.db", sorted[0]);
        Assert.Equal(@"C:\Test\Exchange.db", sorted[1]);
        Assert.Equal(@"C:\Test\Windows.db", sorted[2]);
    }

    [Fact]
    public void Sort_WithNumericVersions_ShouldSortDescending()
    {
        var databases = new[]
        {
            @"C:\Test\Product 10.db",
            @"C:\Test\Product 2.db",
            @"C:\Test\Product 20.db"
        };

        var sorted = DatabasePathSorter.Sort(databases);

        Assert.Equal(3, sorted.Count);
        Assert.Equal(@"C:\Test\Product 20.db", sorted[0]);
        Assert.Equal(@"C:\Test\Product 10.db", sorted[1]);
        Assert.Equal(@"C:\Test\Product 2.db", sorted[2]);
    }

    [Fact]
    public void Sort_WithProductNameAndVersion_ShouldSortByProductAscendingVersionDescending()
    {
        var databases = new[]
        {
            @"C:\Test\Windows 2016.db",
            @"C:\Test\Windows 2019.db",
            @"C:\Test\Exchange 2019.db",
            @"C:\Test\Exchange 2016.db"
        };

        var sorted = DatabasePathSorter.Sort(databases);

        Assert.Equal(4, sorted.Count);
        Assert.Equal(@"C:\Test\Exchange 2019.db", sorted[0]);
        Assert.Equal(@"C:\Test\Exchange 2016.db", sorted[1]);
        Assert.Equal(@"C:\Test\Windows 2019.db", sorted[2]);
        Assert.Equal(@"C:\Test\Windows 2016.db", sorted[3]);
    }

    [Fact]
    public void Sort_WithSingleDatabase_ShouldReturnSameDatabase()
    {
        var databases = new[] { @"C:\Test\Windows 2019.db" };

        var sorted = DatabasePathSorter.Sort(databases);

        Assert.Single(sorted);
        Assert.Equal(@"C:\Test\Windows 2019.db", sorted[0]);
    }
}

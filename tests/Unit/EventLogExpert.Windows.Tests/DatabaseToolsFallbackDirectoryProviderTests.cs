// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.WindowsPlatform.DatabaseTools;
using Xunit;

namespace EventLogExpert.Windows.Tests;

public sealed class DatabaseToolsFallbackDirectoryProviderTests
{
    private static string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [Fact]
    public void GetFallbackDirectories_DelegatesToKnownFolders_ForDocumentsRole()
    {
        var provider = new DatabaseToolsFallbackDirectoryProvider();

        Assert.Equal(new[] { DocumentsFolder }, provider.GetFallbackDirectories(DatabaseToolsPickRole.Output));
    }

    [Fact]
    public void KnownFoldersDocuments_ReturnsEnvironmentMyDocumentsPath()
    {
        Assert.Equal(DocumentsFolder, KnownFolders.Documents);
    }

    [Fact]
    public void KnownFoldersDownloads_DoesNotThrowAndReturnsNullOrRootedPath()
    {
        string? downloads = KnownFolders.Downloads;

        Assert.True(downloads is null || Path.IsPathRooted(downloads));
    }

    [Fact]
    public void SelectFallbackDirectories_ForUnknownRole_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DatabaseToolsFallbackDirectoryProvider.SelectFallbackDirectories((DatabaseToolsPickRole)999, @"C:\Downloads", @"C:\Documents"));
    }

    [Theory]
    [InlineData(DatabaseToolsPickRole.Source, @"C:\Downloads", @"C:\Documents", new[] { @"C:\Downloads", @"C:\Documents" })]
    [InlineData(DatabaseToolsPickRole.Source, null, @"C:\Documents", new[] { @"C:\Documents" })]
    [InlineData(DatabaseToolsPickRole.Source, @"C:\Downloads", null, new[] { @"C:\Downloads" })]
    [InlineData(DatabaseToolsPickRole.Source, null, null, new string[0])]
    [InlineData(DatabaseToolsPickRole.Output, @"C:\Downloads", @"C:\Documents", new[] { @"C:\Documents" })]
    [InlineData(DatabaseToolsPickRole.Output, @"C:\Downloads", null, new string[0])]
    [InlineData(DatabaseToolsPickRole.ExistingDatabase, @"C:\Downloads", @"C:\Documents", new[] { @"C:\Documents" })]
    public void SelectFallbackDirectories_ReturnsOrderedNonNullCandidatesWithDownloadsFirstForSource(
        DatabaseToolsPickRole role,
        string? downloads,
        string? documents,
        string[] expected)
    {
        Assert.Equal(expected, DatabaseToolsFallbackDirectoryProvider.SelectFallbackDirectories(role, downloads, documents));
    }
}

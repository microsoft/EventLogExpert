// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;

namespace EventLogExpert.DatabaseTools.Tests.CreateDatabase;

public sealed class CreateDatabaseSelectModeTests
{
    [Fact]
    public void SelectMode_WhenBothOfflineImageAndSource_OfflineImageWins()
    {
        // Offline must win so validation rejects conflicting source+offline inputs.
        var request = new CreateDatabaseRequest(
            @"C:\out.db", SourcePath: @"C:\src.db", FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\");

        Assert.Equal(CreateDatabaseOperation.CreateDatabaseMode.OfflineImage, CreateDatabaseOperation.SelectMode(request));
    }

    [Fact]
    public void SelectMode_WhenNeitherSourceNorOfflineImage_IsLocal()
    {
        var request = new CreateDatabaseRequest(@"C:\out.db", SourcePath: null, FilterRegex: null, SkipProvidersInFile: null);

        Assert.Equal(CreateDatabaseOperation.CreateDatabaseMode.Local, CreateDatabaseOperation.SelectMode(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SelectMode_WhenOfflineImagePathIsBlank_IsNotOfflineImage(string blank)
    {
        var request = new CreateDatabaseRequest(
            @"C:\out.db", SourcePath: null, FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: blank);

        Assert.Equal(CreateDatabaseOperation.CreateDatabaseMode.Local, CreateDatabaseOperation.SelectMode(request));
    }

    [Fact]
    public void SelectMode_WhenOfflineImagePathSet_IsOfflineImage()
    {
        var request = new CreateDatabaseRequest(
            @"C:\out.db", SourcePath: null, FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\");

        // Offline mode suppresses host-provenance reads without needing a mounted image.
        Assert.Equal(CreateDatabaseOperation.CreateDatabaseMode.OfflineImage, CreateDatabaseOperation.SelectMode(request));
    }

    [Fact]
    public void SelectMode_WhenSourceOnly_IsFileSource()
    {
        var request = new CreateDatabaseRequest(@"C:\out.db", SourcePath: @"C:\src.db", FilterRegex: null, SkipProvidersInFile: null);

        Assert.Equal(CreateDatabaseOperation.CreateDatabaseMode.FileSource, CreateDatabaseOperation.SelectMode(request));
    }
}

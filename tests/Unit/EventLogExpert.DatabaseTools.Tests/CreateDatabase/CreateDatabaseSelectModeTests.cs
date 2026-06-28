// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;

namespace EventLogExpert.DatabaseTools.Tests.CreateDatabase;

public sealed class CreateDatabaseSelectModeTests
{
    [Fact]
    public void SelectMode_WhenBothOfflineImageAndSource_OfflineImageWins()
    {
        // Offline detection wins so the operation's validation can then reject the source+offline combination with a
        // clear message, rather than silently routing to the file source and ignoring the offline image.
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
        // A blank offline path is treated as unset (matching the operation's validation), so the request falls through
        // to local rather than becoming a phantom offline build that then fails a directory check.
        var request = new CreateDatabaseRequest(
            @"C:\out.db", SourcePath: null, FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: blank);

        Assert.Equal(CreateDatabaseOperation.CreateDatabaseMode.Local, CreateDatabaseOperation.SelectMode(request));
    }

    [Fact]
    public void SelectMode_WhenOfflineImagePathSet_IsOfflineImage()
    {
        var request = new CreateDatabaseRequest(
            @"C:\out.db", SourcePath: null, FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\");

        // OfflineImage mode is what suppresses the host-provenance read in the operation; locking it here keeps that
        // carry-forward regression-proof without needing a real mounted image.
        Assert.Equal(CreateDatabaseOperation.CreateDatabaseMode.OfflineImage, CreateDatabaseOperation.SelectMode(request));
    }

    [Fact]
    public void SelectMode_WhenSourceOnly_IsFileSource()
    {
        var request = new CreateDatabaseRequest(@"C:\out.db", SourcePath: @"C:\src.db", FilterRegex: null, SkipProvidersInFile: null);

        Assert.Equal(CreateDatabaseOperation.CreateDatabaseMode.FileSource, CreateDatabaseOperation.SelectMode(request));
    }
}

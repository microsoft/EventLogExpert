// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.ElevationHelper.Operations;

namespace EventLogExpert.DatabaseTools.Tests.ElevationHelper.Operations;

public sealed class ListImageEditionsHandlerTests
{
    [Theory]
    [InlineData("missing.iso", "ISO image file not found:")]
    [InlineData("missing.wim", "WIM image file not found:")]
    public async Task HandleAsync_WhenImageFileDoesNotExist_ReturnsFileNotFoundFailure(string fileName, string expectedPrefix)
    {
        string imagePath = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N") + "_" + fileName);
        await using IpcMessageWriter writer = new(new MemoryStream());
        ListOfflineImageEditionsRequest request = new(imagePath);

        DatabaseToolsResult result = await ListImageEditionsHandler.HandleAsync(
            request,
            writer,
            verbose: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
        Assert.StartsWith(expectedPrefix, result.FailureSummary, StringComparison.Ordinal);
        Assert.Contains(imagePath, result.FailureSummary, StringComparison.Ordinal);
    }
}

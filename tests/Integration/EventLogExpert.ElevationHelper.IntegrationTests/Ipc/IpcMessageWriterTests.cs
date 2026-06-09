// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.ElevationHelper.Ipc;

namespace EventLogExpert.ElevationHelper.IntegrationTests.Ipc;

public sealed class IpcMessageWriterTests
{
    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        using var stream = new MemoryStream();
        var writer = new IpcMessageWriter(stream);

        await writer.DisposeAsync();

        var exception = await Record.ExceptionAsync(async () => await writer.DisposeAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task WriteAsync_AfterDisposeAsync_DoesNotThrow()
    {
        using var stream = new MemoryStream();
        var writer = new IpcMessageWriter(stream);

        await writer.DisposeAsync();

        var exception = await Record.ExceptionAsync(async () =>
            await writer.WriteAsync(new HelloMessage(HelperProcessId: 1, ProtocolVersion: 1), CancellationToken.None));
        Assert.Null(exception);
    }

    [Fact]
    public async Task WriteAsync_BeforeDispose_WritesMessageToStream()
    {
        using var stream = new MemoryStream();
        await using var writer = new IpcMessageWriter(stream);

        await writer.WriteAsync(new HelloMessage(HelperProcessId: 42, ProtocolVersion: 1), CancellationToken.None);

        Assert.NotEqual(0, stream.Length);
    }
}

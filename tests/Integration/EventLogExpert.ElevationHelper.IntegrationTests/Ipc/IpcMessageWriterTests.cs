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

    [Fact]
    public async Task WriteAsync_PendingWhileDisposeRuns_CompletesViaCancellationInsteadOfHanging()
    {
        using var stream = new GatedStream();
        var writer = new IpcMessageWriter(stream);

        var firstWrite = writer.WriteAsync(new HelloMessage(HelperProcessId: 1, ProtocolVersion: 1), CancellationToken.None);

        await stream.FirstWriteStarted.WaitAsync(TestContext.Current.CancellationToken);

        var secondWrite = writer.WriteAsync(new HelloMessage(HelperProcessId: 2, ProtocolVersion: 1), CancellationToken.None);

        var disposeTask = writer.DisposeAsync().AsTask();

        var completed = await Task.WhenAny(secondWrite, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Same(secondWrite, completed);

        stream.ReleaseFirstWrite();

        await firstWrite;
        await disposeTask;
    }

    private sealed class GatedStream : Stream
    {
        private readonly TaskCompletionSource<bool> _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _writeCount;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public Task FirstWriteStarted => _firstWriteStarted.Task;

        public override long Length => 0;

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public void ReleaseFirstWrite() => _releaseFirstWrite.TrySetResult(true);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _writeCount) == 1)
            {
                _firstWriteStarted.TrySetResult(true);

                await _releaseFirstWrite.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

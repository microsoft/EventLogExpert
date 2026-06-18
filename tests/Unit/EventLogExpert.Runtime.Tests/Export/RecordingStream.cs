// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Tests.Export;

internal sealed class RecordingStream : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => TotalBytesWritten;

    public int MaxSingleWriteBytes { get; private set; }

    public override long Position
    {
        get => TotalBytesWritten;
        set => throw new NotSupportedException();
    }

    public long TotalBytesWritten { get; private set; }

    public bool WasDisposed { get; private set; }

    public int WriteCallCount { get; private set; }

    public override void Flush() { }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Record(count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(count);

        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(buffer.Length);

        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        base.Dispose(disposing);
    }

    private void Record(int count)
    {
        WriteCallCount++;
        TotalBytesWritten += count;

        if (count > MaxSingleWriteBytes)
        {
            MaxSingleWriteBytes = count;
        }
    }
}

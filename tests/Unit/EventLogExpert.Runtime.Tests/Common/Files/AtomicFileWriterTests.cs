// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Files;
using System.Text;

namespace EventLogExpert.Runtime.Tests.Common.Files;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _directory;

    public AtomicFileWriterTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"ele-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* best-effort test cleanup. */ }
        catch (UnauthorizedAccessException) { /* best-effort test cleanup. */ }
    }

    [Fact]
    public async Task WriteAsync_CancelledAfterWrite_LeavesExistingDestinationUntouchedAndDeletesTemp()
    {
        var destination = Destination();
        await File.WriteAllTextAsync(destination, "original", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        // Cancel from inside writeContent (after the content is written) so the post-write gate throws before commit.
        Func<Stream, CancellationToken, Task> writeThenCancel = async (stream, token) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("new"), token);
            await cancellation.CancelAsync();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AtomicFileWriter.WriteAsync(destination, writeThenCancel, cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(LeakedTempFiles());
    }

    [Fact]
    public async Task WriteAsync_CancelledBeforeWrite_ThrowsAndCreatesNoFile()
    {
        var destination = Destination();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AtomicFileWriter.WriteAsync(destination, WriteText("x"), cancellation.Token));

        Assert.False(File.Exists(destination));
        Assert.Empty(LeakedTempFiles());
    }

    [Fact]
    public async Task WriteAsync_EmptyDestination_ThrowsArgumentException() =>
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => AtomicFileWriter.WriteAsync("", WriteText("x"), TestContext.Current.CancellationToken));

    [Fact]
    public async Task WriteAsync_ExistingDestination_ReplacesContent()
    {
        var destination = Destination();
        await File.WriteAllTextAsync(destination, "original", TestContext.Current.CancellationToken);

        await AtomicFileWriter.WriteAsync(destination, WriteText("replaced"), TestContext.Current.CancellationToken);

        Assert.Equal("replaced", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(LeakedTempFiles());
    }

    [Fact]
    public async Task WriteAsync_ExistingLongerDestination_IsFullyReplacedByShorterContent()
    {
        // The commit is a whole-file atomic rename, so a shorter payload fully supersedes a longer original with no
        // trailing bytes (unlike a naive truncate-then-write).
        var destination = Destination();
        await File.WriteAllTextAsync(
            destination, "a considerably longer original content string", TestContext.Current.CancellationToken);

        await AtomicFileWriter.WriteAsync(destination, WriteText("short"), TestContext.Current.CancellationToken);

        Assert.Equal("short", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(LeakedTempFiles());
    }

    [Fact]
    public async Task WriteAsync_NewDestination_CreatesFileWithContent()
    {
        var destination = Destination();

        await AtomicFileWriter.WriteAsync(destination, WriteText("hello"), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(destination));
        Assert.Equal("hello", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(LeakedTempFiles());
    }

    [Fact]
    public async Task WriteAsync_NoDirectoryComponent_ThrowsArgumentException() =>
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => AtomicFileWriter.WriteAsync("barefilename.log", WriteText("x"), TestContext.Current.CancellationToken));

    [Fact]
    public async Task WriteAsync_NoneToken_CommitsCompletedWriteEvenWhenASeparateSourceIsCancelled()
    {
        // Mirrors ExportEventsAsync: the write is scoped to a caller CTS, but AtomicFileWriter receives None, so a
        // Cancel that lands after the content is written cannot abort the atomic commit - the completed file is saved.
        var destination = Destination();
        using var cancellation = new CancellationTokenSource();

        Func<Stream, CancellationToken, Task> writeThenCancelSeparate = async (stream, _) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("exported"), TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
        };

        await AtomicFileWriter.WriteAsync(destination, writeThenCancelSeparate, CancellationToken.None);

        Assert.Equal("exported", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(LeakedTempFiles());
    }

    [Fact]
    public async Task WriteAsync_NullWriteContent_ThrowsArgumentNullException() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AtomicFileWriter.WriteAsync(Destination(), null!, TestContext.Current.CancellationToken));

    [Fact]
    public async Task WriteAsync_WriteContentThrows_LeavesExistingDestinationUntouchedAndDeletesTemp()
    {
        var destination = Destination();
        await File.WriteAllTextAsync(destination, "original", TestContext.Current.CancellationToken);

        Func<Stream, CancellationToken, Task> throwing = (_, _) => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AtomicFileWriter.WriteAsync(destination, throwing, TestContext.Current.CancellationToken));

        Assert.Equal("original", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(LeakedTempFiles());
    }

    private static Func<Stream, CancellationToken, Task> WriteText(string text) =>
        (stream, token) => stream.WriteAsync(Encoding.UTF8.GetBytes(text), token).AsTask();

    private string Destination(string name = "output.log") => Path.Combine(_directory, name);

    private IReadOnlyList<string> LeakedTempFiles() => Directory.GetFiles(_directory, "*.tmp");
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Concurrency;

namespace EventLogExpert.Logging.Tests.Concurrency;

public sealed class InterProcessFileLockTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"elx-filelock-{Guid.NewGuid():N}");

    public InterProcessFileLockTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* best-effort cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
    }

    [Fact]
    public void Run_ReleasesLockBetweenSequentialCalls()
    {
        var target = TargetPath();
        var fileLock = new InterProcessFileLock("ProviderDbSchema", target);

        fileLock.Run(TimeSpan.FromSeconds(5), static () => { });

        // A second sequential acquisition must succeed, proving the handle was released on the first Run's completion.
        var ranAgain = false;
        fileLock.Run(TimeSpan.FromSeconds(5), () => ranAgain = true);

        Assert.True(ranAgain);
    }

    [Fact]
    public void Run_RunsActionAndLeavesSentinelFileOnDisk()
    {
        var target = TargetPath();
        var lockPath = $"{target}.ProviderDbSchema.lock";
        var fileLock = new InterProcessFileLock("ProviderDbSchema", target);

        var ran = false;
        fileLock.Run(TimeSpan.FromSeconds(5), () => ran = true);

        Assert.True(ran);

        // The sentinel is deliberately never deleted (deleting it would open a pending-deletion race).
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public void Run_WhenHeldByAnotherInstance_ThrowsOnTimeout()
    {
        var target = TargetPath();
        var first = new InterProcessFileLock("ProviderDbSchema", target);
        var second = new InterProcessFileLock("ProviderDbSchema", target);

        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var holder = StartHolder(first, acquired, release, TestContext.Current.CancellationToken);

        Assert.True(acquired.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Throws<TimeoutException>(() => second.Run(TimeSpan.FromMilliseconds(300), static () => { }));

        release.Set();
        holder.Join();
    }

    [Fact]
    public void TryRun_TwoInstancesSamePath_SecondBlockedWhileFirstHolds_ThenSucceeds()
    {
        var target = TargetPath();
        var first = new InterProcessFileLock("ProviderDbSchema", target);
        var second = new InterProcessFileLock("ProviderDbSchema", target);

        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var holder = StartHolder(first, acquired, release, TestContext.Current.CancellationToken);

        Assert.True(acquired.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.False(second.TryRun(TimeSpan.FromMilliseconds(300), static () => { }));

        release.Set();
        holder.Join();

        // The lock is released when the holder's stream is disposed, so the second instance can now acquire it.
        Assert.True(second.TryRun(TimeSpan.FromSeconds(10), static () => { }));
    }

    private static Thread StartHolder(
        InterProcessFileLock fileLock,
        ManualResetEventSlim acquired,
        ManualResetEventSlim release,
        CancellationToken cancellationToken)
    {
        var holder = new Thread(() => fileLock.Run(TimeSpan.FromSeconds(10), () =>
        {
            acquired.Set();
            release.Wait(TimeSpan.FromSeconds(10), cancellationToken);
        }));

        holder.Start();

        return holder;
    }

    private string TargetPath() =>
        Path.Combine(_directory, $"{Guid.NewGuid():N}.db");
}

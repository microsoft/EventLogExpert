// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Concurrency;

namespace EventLogExpert.Logging.Tests.Concurrency;

public sealed class InterProcessFileLockTests : IDisposable
{
    private static readonly TimeSpan s_acquireTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_contendedTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(10);

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

        fileLock.Run(s_acquireTimeout, static () => { });

        var ranAgain = false;
        fileLock.Run(s_acquireTimeout, () => ranAgain = true);

        Assert.True(ranAgain);
    }

    [Fact]
    public void Run_RunsActionAndLeavesSentinelFileOnDisk()
    {
        var target = TargetPath();
        var lockPath = $"{target}.ProviderDbSchema.lock";
        var fileLock = new InterProcessFileLock("ProviderDbSchema", target);

        var ran = false;
        fileLock.Run(s_acquireTimeout, () => ran = true);

        Assert.True(ran);

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

        Assert.True(acquired.Wait(s_testTimeout, TestContext.Current.CancellationToken));
        Assert.Throws<TimeoutException>(() => second.Run(s_contendedTimeout, static () => { }));

        release.Set();
        holder.Join();
    }

    [Fact]
    public void TryRun_InfiniteTimeout_WaitsForHeldLockThenAcquires()
    {
        const int ReleaseDelayMilliseconds = 300;

        var target = TargetPath();
        var first = new InterProcessFileLock("ProviderDbSchema", target);
        var second = new InterProcessFileLock("ProviderDbSchema", target);

        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var holder = StartHolder(first, acquired, release, TestContext.Current.CancellationToken);

        Assert.True(acquired.Wait(s_testTimeout, TestContext.Current.CancellationToken));

        var releaser = new Thread(() =>
        {
            Thread.Sleep(ReleaseDelayMilliseconds);
            release.Set();
        });
        releaser.Start();

        var ranAgain = false;
        Assert.True(second.TryRun(Timeout.InfiniteTimeSpan, () => ranAgain = true));
        Assert.True(ranAgain);

        holder.Join();
        releaser.Join();
    }

    [Theory]
    [InlineData(-20000)]
    [InlineData(-15000)]
    [InlineData(-1)]
    public void TryRun_NegativeTimeoutBelowInfinite_ThrowsArgumentOutOfRange(long ticks)
    {
        var fileLock = new InterProcessFileLock("ProviderDbSchema", TargetPath());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => fileLock.TryRun(TimeSpan.FromTicks(ticks), static () => { }));
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

        Assert.True(acquired.Wait(s_testTimeout, TestContext.Current.CancellationToken));
        Assert.False(second.TryRun(s_contendedTimeout, static () => { }));

        release.Set();
        holder.Join();

        Assert.True(second.TryRun(s_testTimeout, static () => { }));
    }

    private static Thread StartHolder(
        InterProcessFileLock fileLock,
        ManualResetEventSlim acquired,
        ManualResetEventSlim release,
        CancellationToken cancellationToken)
    {
        var holder = new Thread(() => fileLock.Run(s_testTimeout, () =>
        {
            acquired.Set();
            release.Wait(s_testTimeout, cancellationToken);
        }));

        holder.Start();

        return holder;
    }

    private string TargetPath() =>
        Path.Combine(_directory, $"{Guid.NewGuid():N}.db");
}

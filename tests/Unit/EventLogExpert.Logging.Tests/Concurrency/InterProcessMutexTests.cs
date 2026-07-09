// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Concurrency;
using System.Security.Cryptography;
using System.Text;

namespace EventLogExpert.Logging.Tests.Concurrency;

public sealed class InterProcessMutexTests
{
    [Fact]
    public void DeriveName_DebugLogScope_MatchesLegacyFileLogSinkFormat()
    {
        var path = @"C:\Logs\EventLogExpert\debug.log";

        var canonical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToUpperInvariant())), 0, 8);
        var expected = $"Local\\EventLogExpert.DebugLog.{expectedHash}";

        Assert.Equal(expected, InterProcessMutex.DeriveName("DebugLog", path));
    }

    [Fact]
    public void DeriveName_DifferentScopes_ProduceDifferentNames()
    {
        var first = InterProcessMutex.DeriveName("ScopeA", @"C:\a\b");
        var second = InterProcessMutex.DeriveName("ScopeB", @"C:\a\b");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DeriveName_EquivalentPaths_ProduceSameName()
    {
        var withoutSeparator = InterProcessMutex.DeriveName("Scope", @"C:\a\b");
        var withSeparator = InterProcessMutex.DeriveName("Scope", @"C:\a\b\");

        Assert.Equal(withoutSeparator, withSeparator);
    }

    [Fact]
    public void Run_WhenHeldByAnotherInstance_ThrowsOnTimeout()
    {
        var key = UniqueKey();

        using var first = new InterProcessMutex("MutexScope", key);
        using var second = new InterProcessMutex("MutexScope", key);
        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var holder = StartHolder(first, acquired, release, TestContext.Current.CancellationToken);

        Assert.True(acquired.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Throws<TimeoutException>(() => second.Run(TimeSpan.FromMilliseconds(300), static () => { }));

        release.Set();
        holder.Join();
    }

    [Fact]
    public void TryRun_AfterMutexAbandonedByDeadThread_Recovers()
    {
        var key = UniqueKey();
        var name = InterProcessMutex.DeriveName("AbandonScope", key);

        // Keep the named object alive across the owning thread's death so the abandoned state (not object destruction)
        // is what the next acquirer observes.
        using var keepAlive = new Mutex(false, name);

        var owningThread = new Thread(() =>
        {
            var owner = new Mutex(false, name);
            owner.WaitOne();

            // Exit while still owning -> Windows marks the mutex abandoned.
        });

        owningThread.Start();
        owningThread.Join();

        using var mutex = new InterProcessMutex("AbandonScope", key);
        var ran = false;

        Assert.True(mutex.TryRun(TimeSpan.FromSeconds(5), () => ran = true));
        Assert.True(ran);
    }

    [Fact]
    public void TryRun_TwoInstancesSameName_SecondBlockedWhileFirstHolds_ThenSucceeds()
    {
        var key = UniqueKey();

        using var first = new InterProcessMutex("MutexScope", key);
        using var second = new InterProcessMutex("MutexScope", key);
        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var holder = StartHolder(first, acquired, release, TestContext.Current.CancellationToken);

        Assert.True(acquired.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // Separate handle to the same named kernel object cannot acquire while the first holds it - proving true
        // interprocess coordination rather than an in-process lock.
        Assert.False(second.TryRun(TimeSpan.FromMilliseconds(300), static () => { }));

        release.Set();
        holder.Join();

        Assert.True(second.TryRun(TimeSpan.FromSeconds(10), static () => { }));
    }

    private static Thread StartHolder(
        InterProcessMutex mutex,
        ManualResetEventSlim acquired,
        ManualResetEventSlim release,
        CancellationToken cancellationToken)
    {
        var holder = new Thread(() => mutex.Run(TimeSpan.FromSeconds(10), () =>
        {
            acquired.Set();
            release.Wait(TimeSpan.FromSeconds(10), cancellationToken);
        }));

        holder.Start();

        return holder;
    }

    private static string UniqueKey() =>
        Path.Combine(Path.GetTempPath(), $"elx-mutex-{Guid.NewGuid():N}");
}

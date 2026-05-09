// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;

namespace EventLogExpert.Eventing.Tests.Resolvers;

public sealed class EventXmlResolverTests
{
    [Fact]
    public async Task ClearAll_RemovesEveryEntry()
    {
        var resolver = CreateTrackingResolver(_ => "<xml/>", out var getResolveCallCount);

        var evt = CreateEvent(recordId: 1);
        await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);
        Assert.Equal(1, getResolveCallCount());

        resolver.ClearAll();

        await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);
        Assert.Equal(2, getResolveCallCount());
    }

    [Fact]
    public async Task ClearXmlCacheForLog_RemovesEntriesForThatLogOnly()
    {
        var resolver = CreateTrackingResolver(
            key => $"<xml log='{key.OwningLog}' id='{key.RecordId}'/>",
            out var getResolveCallCount);

        var evtA1 = CreateEvent(recordId: 1, owningLog: "A");
        var evtA2 = CreateEvent(recordId: 2, owningLog: "A");
        var evtB1 = CreateEvent(recordId: 1, owningLog: "B");

        await resolver.GetXmlAsync(evtA1, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtA2, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtB1, TestContext.Current.CancellationToken);
        Assert.Equal(3, getResolveCallCount());

        resolver.ClearXmlCacheForLog("A");

        // B is untouched.
        await resolver.GetXmlAsync(evtB1, TestContext.Current.CancellationToken);
        Assert.Equal(3, getResolveCallCount());

        // A entries were evicted; both are re-resolved.
        await resolver.GetXmlAsync(evtA1, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtA2, TestContext.Current.CancellationToken);
        Assert.Equal(5, getResolveCallCount());
    }

    [Fact]
    public async Task GetXmlAsync_AtCapacity_EvictsLeastRecentlyUsed()
    {
        // Initial = max = 2, so eviction kicks in once we exceed 2 entries.
        var resolver = CreateBoundedTrackingResolver(
            key => $"<xml log='{key.OwningLog}' id='{key.RecordId}'/>",
            out var getResolveCallCount,
            initialCapacity: 2,
            maxCapacity: 2);

        var evtA = CreateEvent(recordId: 1, owningLog: "A");
        var evtB = CreateEvent(recordId: 2, owningLog: "B");
        var evtC = CreateEvent(recordId: 3, owningLog: "C");

        await resolver.GetXmlAsync(evtA, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtB, TestContext.Current.CancellationToken);
        // Touch A so B becomes the least-recently-used entry.
        await resolver.GetXmlAsync(evtA, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtC, TestContext.Current.CancellationToken);

        // A and C should still be cached (B was evicted as LRU); A re-request must not re-resolve.
        await resolver.GetXmlAsync(evtA, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtC, TestContext.Current.CancellationToken);
        Assert.Equal(3, getResolveCallCount());

        // Re-requesting B triggers another resolve since it was evicted.
        await resolver.GetXmlAsync(evtB, TestContext.Current.CancellationToken);
        Assert.Equal(4, getResolveCallCount());
    }

    [Fact]
    public async Task GetXmlAsync_ConcurrentRequestsForSameKey_ResolveOnce()
    {
        using var gate = new ManualResetEventSlim(initialState: false);
        var resolver = CreateTrackingResolver(
            _ =>
            {
                gate.Wait(TimeSpan.FromSeconds(5));

                return "<xml/>";
            },
            out var getResolveCallCount);

        var evt = CreateEvent(recordId: 7);

        var t1 = resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken).AsTask();
        var t2 = resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken).AsTask();
        var t3 = resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken).AsTask();

        gate.Set();
        var results = await Task.WhenAll(t1, t2, t3);

        Assert.All(results, r => Assert.Equal("<xml/>", r));
        Assert.Equal(1, getResolveCallCount());
    }

    [Fact]
    public async Task GetXmlAsync_OnCacheMiss_ResolvesAndCachesResult()
    {
        var resolver = CreateTrackingResolver(
            key => $"<xml id='{key.RecordId}'/>",
            out var getResolveCallCount);
        var evt = CreateEvent(recordId: 42);

        var first = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);
        var second = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);

        Assert.Equal("<xml id='42'/>", first);
        Assert.Equal("<xml id='42'/>", second);
        Assert.Equal(1, getResolveCallCount());
    }

    [Fact]
    public async Task GetXmlAsync_WhenEventHasPreRenderedXml_ReturnsItWithoutResolving()
    {
        var resolver = CreateTrackingResolver(_ => "should-not-be-called", out var getResolveCallCount);
        var evt = CreateEvent(recordId: 1) with { Xml = "<pre-rendered/>" };

        var result = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);

        Assert.Equal("<pre-rendered/>", result);
        Assert.Equal(0, getResolveCallCount());
    }

    [Fact]
    public async Task GetXmlAsync_WhenOwningLogIsEmpty_ReturnsEmptyWithoutResolving()
    {
        var resolver = CreateTrackingResolver(_ => "x", out var getResolveCallCount);
        var evt = CreateEvent(recordId: 1, owningLog: string.Empty);

        var result = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, getResolveCallCount());
    }

    [Fact]
    public async Task GetXmlAsync_WhenRecordIdIsNull_ReturnsEmptyWithoutResolving()
    {
        var resolver = CreateTrackingResolver(_ => "x", out var getResolveCallCount);
        var evt = CreateEvent(recordId: null);

        var result = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, getResolveCallCount());
    }

    [Fact]
    public async Task GetXmlAsync_WhenResolveThrows_EvictsEntryAndAllowsRetry()
    {
        int callCount = 0;
        var resolver = CreateTrackingResolver(
            _ =>
            {
                callCount++;

                return callCount == 1 ? throw new InvalidOperationException("boom") : "<xml/>";
            },
            out _);

        var evt = CreateEvent(recordId: 99);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken));

        var result = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);
        Assert.Equal("<xml/>", result);
        Assert.Equal(2, callCount);
    }

    private static EventXmlResolver CreateBoundedTrackingResolver(
        Func<ResolveKey, string> resolve,
        out Func<int> getResolveCallCount,
        int initialCapacity,
        int maxCapacity)
    {
        int resolveCount = 0;

        getResolveCallCount = () => Volatile.Read(ref resolveCount);

        return new EventXmlResolver(
            (owningLog, recordId, LogPathType) =>
            {
                Interlocked.Increment(ref resolveCount);

                return resolve(new ResolveKey(owningLog, recordId, LogPathType));
            },
            initialCapacity,
            maxCapacity);
    }

    private static DisplayEventModel CreateEvent(long? recordId, string owningLog = "TestLog") =>
        new(owningLog, LogPathType.Channel)
        {
            RecordId = recordId,
            Xml = string.Empty
        };

    private static EventXmlResolver CreateTrackingResolver(
        Func<ResolveKey, string> resolve,
        out Func<int> getResolveCallCount)
    {
        int resolveCount = 0;

        getResolveCallCount = () => Volatile.Read(ref resolveCount);

        return new EventXmlResolver(
            (owningLog, recordId, LogPathType) =>
            {
                Interlocked.Increment(ref resolveCount);

                return resolve(new ResolveKey(owningLog, recordId, LogPathType));
            });
    }

    private readonly record struct ResolveKey(string OwningLog, long RecordId, LogPathType LogPathType);
}

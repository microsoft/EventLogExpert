// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.Tests.EventResolvers;

public sealed class EventXmlResolverTests
{
    [Fact]
    public async Task ClearAll_RemovesEveryEntry()
    {
        var resolver = new TrackingResolver(_ => "<xml/>");

        var evt = CreateEvent(recordId: 1);
        await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);
        Assert.Equal(1, resolver.ResolveCallCount);

        resolver.ClearAll();

        await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);
        Assert.Equal(2, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task ClearLog_RemovesEntriesForThatLogOnly()
    {
        var resolver = new TrackingResolver(key => $"<xml log='{key.OwningLog}' id='{key.RecordId}'/>");

        var evtA1 = CreateEvent(recordId: 1, owningLog: "A");
        var evtA2 = CreateEvent(recordId: 2, owningLog: "A");
        var evtB1 = CreateEvent(recordId: 1, owningLog: "B");

        await resolver.GetXmlAsync(evtA1, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtA2, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtB1, TestContext.Current.CancellationToken);
        Assert.Equal(3, resolver.ResolveCallCount);

        resolver.ClearLog("A");

        // B is untouched.
        await resolver.GetXmlAsync(evtB1, TestContext.Current.CancellationToken);
        Assert.Equal(3, resolver.ResolveCallCount);

        // A entries were evicted; both are re-resolved.
        await resolver.GetXmlAsync(evtA1, TestContext.Current.CancellationToken);
        await resolver.GetXmlAsync(evtA2, TestContext.Current.CancellationToken);
        Assert.Equal(5, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task GetXmlAsync_AtCapacity_EvictsLeastRecentlyUsed()
    {
        // Initial = max = 2, so eviction kicks in once we exceed 2 entries.
        var resolver = new BoundedTrackingResolver(initialCapacity: 2, maxCapacity: 2);

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
        Assert.Equal(3, resolver.ResolveCallCount);

        // Re-requesting B triggers another resolve since it was evicted.
        await resolver.GetXmlAsync(evtB, TestContext.Current.CancellationToken);
        Assert.Equal(4, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task GetXmlAsync_ConcurrentRequestsForSameKey_ResolveOnce()
    {
        using var gate = new ManualResetEventSlim(initialState: false);
        var resolver = new TrackingResolver(_ =>
        {
            gate.Wait(TimeSpan.FromSeconds(5));

            return "<xml/>";
        });

        var evt = CreateEvent(recordId: 7);

        var t1 = resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken).AsTask();
        var t2 = resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken).AsTask();
        var t3 = resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken).AsTask();

        gate.Set();
        var results = await Task.WhenAll(t1, t2, t3);

        Assert.All(results, r => Assert.Equal("<xml/>", r));
        Assert.Equal(1, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task GetXmlAsync_OnCacheMiss_ResolvesAndCachesResult()
    {
        var resolver = new TrackingResolver(key => $"<xml id='{key.RecordId}'/>");
        var evt = CreateEvent(recordId: 42);

        var first = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);
        var second = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);

        Assert.Equal("<xml id='42'/>", first);
        Assert.Equal("<xml id='42'/>", second);
        Assert.Equal(1, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task GetXmlAsync_WhenEventHasPreRenderedXml_ReturnsItWithoutResolving()
    {
        var resolver = new TrackingResolver(_ => "should-not-be-called");
        var evt = CreateEvent(recordId: 1) with { Xml = "<pre-rendered/>" };

        var result = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);

        Assert.Equal("<pre-rendered/>", result);
        Assert.Equal(0, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task GetXmlAsync_WhenOwningLogIsEmpty_ReturnsEmptyWithoutResolving()
    {
        var resolver = new TrackingResolver(_ => "x");
        var evt = CreateEvent(recordId: 1, owningLog: string.Empty);

        var result = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task GetXmlAsync_WhenRecordIdIsNull_ReturnsEmptyWithoutResolving()
    {
        var resolver = new TrackingResolver(_ => "x");
        var evt = CreateEvent(recordId: null);

        var result = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task GetXmlAsync_WhenResolveThrows_EvictsEntryAndAllowsRetry()
    {
        int callCount = 0;
        var resolver = new TrackingResolver(_ =>
        {
            callCount++;

            return callCount == 1 ? throw new InvalidOperationException("boom") : "<xml/>";
        });

        var evt = CreateEvent(recordId: 99);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken));

        var result = await resolver.GetXmlAsync(evt, TestContext.Current.CancellationToken);
        Assert.Equal("<xml/>", result);
        Assert.Equal(2, callCount);
    }

    private static DisplayEventModel CreateEvent(long? recordId, string owningLog = "TestLog") =>
        new(owningLog, PathType.LogName)
        {
            RecordId = recordId,
            Xml = string.Empty
        };

    private sealed class BoundedTrackingResolver(int initialCapacity, int maxCapacity)
        : EventXmlResolver(initialCapacity, maxCapacity)
    {
        private int _resolveCallCount;

        public int ResolveCallCount => Volatile.Read(ref _resolveCallCount);

        protected override string ResolveXml(string owningLog, long recordId, PathType pathType)
        {
            Interlocked.Increment(ref _resolveCallCount);

            return $"<xml log='{owningLog}' id='{recordId}'/>";
        }
    }

    private class TrackingResolver(Func<ResolveKey, string> resolve) : EventXmlResolver
    {
        private int _resolveCallCount;

        public int ResolveCallCount => Volatile.Read(ref _resolveCallCount);

        protected override string ResolveXml(string owningLog, long recordId, PathType pathType)
        {
            Interlocked.Increment(ref _resolveCallCount);

            return resolve(new ResolveKey(owningLog, recordId, pathType));
        }
    }

    private sealed record ResolveKey(string OwningLog, long RecordId, PathType PathType);
}

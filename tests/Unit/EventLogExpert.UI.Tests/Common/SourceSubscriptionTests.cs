// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common;

namespace EventLogExpert.UI.Tests.Common;

public sealed class SourceSubscriptionTests
{
    [Fact]
    public void Changed_AfterDispose_DoesNotRender()
    {
        Action? handler = null;
        var renders = 0;
        var subscription = new SourceSubscription(
            subscribe: h => handler = h,
            unsubscribe: _ => { },
            render: () => { renders++; return Task.CompletedTask; });
        subscription.Dispose();

        handler!();

        Assert.Equal(0, renders);
    }

    [Fact]
    public void Changed_RendersOncePerRaise()
    {
        Action? handler = null;
        var renders = 0;
        using var subscription = new SourceSubscription(
            subscribe: h => handler = h,
            unsubscribe: _ => { },
            render: () => { renders++; return Task.CompletedTask; });

        handler!();

        Assert.Equal(1, renders);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var unsubscribes = 0;
        var subscription = new SourceSubscription(
            subscribe: _ => { },
            unsubscribe: _ => unsubscribes++,
            render: () => Task.CompletedTask);

        subscription.Dispose();
        subscription.Dispose();

        Assert.Equal(1, unsubscribes);
    }

    [Fact]
    public void Dispose_Unsubscribes()
    {
        var subscribed = false;
        var subscription = new SourceSubscription(
            subscribe: _ => subscribed = true,
            unsubscribe: _ => subscribed = false,
            render: () => Task.CompletedTask);
        Assert.True(subscribed);

        subscription.Dispose();

        Assert.False(subscribed);
    }
}

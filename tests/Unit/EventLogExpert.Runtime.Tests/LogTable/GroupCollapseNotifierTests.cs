// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.LogTable;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class GroupCollapseNotifierTests
{
    [Fact]
    public void Raise_InvokesSubscribers()
    {
        var notifier = new GroupCollapseNotifier(Substitute.For<ITraceLogger>());
        var raised = 0;
        notifier.Requested += () => raised++;

        notifier.Raise();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Raise_IsolatesAThrowingSubscriber()
    {
        var notifier = new GroupCollapseNotifier(Substitute.For<ITraceLogger>());
        var reachedSecond = 0;
        notifier.Requested += () => throw new InvalidOperationException("subscriber blew up");
        notifier.Requested += () => reachedSecond++;

        notifier.Raise();

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Raise_WithNoSubscribers_DoesNotThrow()
    {
        var notifier = new GroupCollapseNotifier(Substitute.For<ITraceLogger>());

        notifier.Raise();
    }
}

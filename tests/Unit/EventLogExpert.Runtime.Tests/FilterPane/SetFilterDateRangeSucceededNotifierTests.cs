// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterPane;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class SetFilterDateRangeSucceededNotifierTests
{
    [Fact]
    public void Raise_InvokesSubscribers()
    {
        var notifier = new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>());
        var raised = 0;
        notifier.Succeeded += () => raised++;

        notifier.Raise();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Raise_IsolatesAThrowingSubscriber()
    {
        var notifier = new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>());
        var reachedSecond = 0;
        notifier.Succeeded += () => throw new InvalidOperationException("subscriber blew up");
        notifier.Succeeded += () => reachedSecond++;

        notifier.Raise();

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Raise_WithNoSubscribers_DoesNotThrow()
    {
        var notifier = new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>());

        notifier.Raise();
    }
}

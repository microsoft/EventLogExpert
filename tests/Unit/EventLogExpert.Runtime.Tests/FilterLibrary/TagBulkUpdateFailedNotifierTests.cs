// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class TagBulkUpdateFailedNotifierTests
{
    [Fact]
    public void Raise_InvokesSubscribers()
    {
        var notifier = new TagBulkUpdateFailedNotifier(Substitute.For<ITraceLogger>());
        var raised = 0;
        notifier.Failed += () => raised++;

        notifier.Raise();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Raise_IsolatesAThrowingSubscriber()
    {
        var notifier = new TagBulkUpdateFailedNotifier(Substitute.For<ITraceLogger>());
        var reachedSecond = 0;
        notifier.Failed += () => throw new InvalidOperationException("subscriber blew up");
        notifier.Failed += () => reachedSecond++;

        notifier.Raise();

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Raise_WithNoSubscribers_DoesNotThrow()
    {
        var notifier = new TagBulkUpdateFailedNotifier(Substitute.For<ITraceLogger>());

        notifier.Raise();
    }
}

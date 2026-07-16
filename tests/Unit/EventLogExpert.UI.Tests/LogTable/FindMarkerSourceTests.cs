// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.UI.LogTable.Find;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class FindMarkerSourceTests
{
    [Fact]
    public void Clear_ResetsOwnerAndTicksAndRaisesMarksChanged()
    {
        var source = new FindMarkerSource();
        source.Publish(EventLogId.Create(), [5L]);
        int raised = 0;
        source.MarksChanged += (_, _) => raised++;

        source.Clear();

        Assert.Null(source.Owner);
        Assert.Empty(source.Ticks);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Clear_WhenAlreadyEmpty_DoesNotRaise()
    {
        var source = new FindMarkerSource();
        int raised = 0;
        source.MarksChanged += (_, _) => raised++;

        source.Clear();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Publish_NullTicks_Throws()
    {
        var source = new FindMarkerSource();

        Assert.Throws<ArgumentNullException>(() => source.Publish(EventLogId.Create(), null!));
    }

    [Fact]
    public void Publish_ReplacesTheOwnerAndTicksOfAPriorPublish()
    {
        var source = new FindMarkerSource();
        var first = EventLogId.Create();
        var second = EventLogId.Create();

        source.Publish(first, [1L, 2L]);
        source.Publish(second, [9L]);

        Assert.Equal(second, source.Owner);
        Assert.Equal([9L], source.Ticks);
    }

    [Fact]
    public void Publish_SetsOwnerAndTicksAndRaisesMarksChanged()
    {
        var source = new FindMarkerSource();
        var owner = EventLogId.Create();
        int raised = 0;
        source.MarksChanged += (_, _) => raised++;

        source.Publish(owner, [10L, 20L, 30L]);

        Assert.Equal(owner, source.Owner);
        Assert.Equal([10L, 20L, 30L], source.Ticks);
        Assert.Equal(1, raised);
    }
}

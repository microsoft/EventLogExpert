// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventDataViewTests
{
    [Fact]
    public void DefaultValues_AreNone()
    {
        var view = new EventDataView(default, Schema("<template><data name=\"A\"/></template>"));

        Assert.Equal(EventDataKind.None, view.Kind);
        Assert.Equal(0, view.Count);
    }

    [Fact]
    public void Empty_IsNone()
    {
        Assert.Equal(EventDataKind.None, EventDataView.Empty.Kind);
        Assert.Equal(0, EventDataView.Empty.Count);
        Assert.False(EventDataView.Empty.TryGetValue("A", out _));
    }

    [Fact]
    public void Enumeration_OverEmptyOrNone_YieldsNothingWithoutThrowing()
    {
        int count = 0;

        foreach (EventDataView.Field field in EventDataView.Empty)
        {
            count++;
        }

        var none = new EventDataView(default, Schema("<template><data name=\"A\"/></template>"));

        foreach (EventDataView.Field field in none)
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Fact]
    public void ResolvedEvent_DefaultEventData_IsNone()
    {
        var resolved = new ResolvedEvent("log", LogPathType.File);

        Assert.Equal(EventDataKind.None, resolved.EventData.Kind);
    }

    [Fact]
    public void TryGetName_AndEnumeration_YieldNamesInOrder()
    {
        TemplateFieldSchema schema = Schema("<template><data name=\"A\"/><data name=\"B\"/></template>");
        var values = ImmutableArray.Create(1, (EventProperty)2);
        var view = new EventDataView(values, schema);

        Assert.True(view.TryGetName(0, out string name0));
        Assert.Equal("A", name0);
        Assert.True(view.TryGetName(1, out string name1));
        Assert.Equal("B", name1);
        Assert.False(view.TryGetName(2, out _));

        var names = new List<string>();

        foreach (EventDataView.Field field in view)
        {
            names.Add(field.Name);
        }

        Assert.Equal(["A", "B"], names);
    }

    [Fact]
    public void TryGetValue_AllOrdering_WhenCountMatchesAll()
    {
        TemplateFieldSchema schema = Schema(
            "<template><data name=\"Len\" inType=\"win:UInt32\"/><data name=\"Payload\" length=\"Len\"/></template>");
        var values = ImmutableArray.Create(7u, (EventProperty)"data");
        var view = new EventDataView(values, schema);

        Assert.True(view.TryGetValue("Len", out EventFieldValue len));
        Assert.True(len.TryGetUInt64(out ulong lenValue));
        Assert.Equal(7UL, lenValue);

        Assert.True(view.TryGetValue("Payload", out EventFieldValue payload));
        Assert.Equal("data", payload.AsString());
    }

    [Fact]
    public void TryGetValue_ByName_VisibleOrdering()
    {
        TemplateFieldSchema schema = Schema("<template><data name=\"A\"/><data name=\"B\"/></template>");
        var values = ImmutableArray.Create(42, (EventProperty)"hello");
        var view = new EventDataView(values, schema);

        Assert.Equal(EventDataKind.EventData, view.Kind);
        Assert.Equal(2, view.Count);

        Assert.True(view.TryGetValue("A", out EventFieldValue a));
        Assert.True(a.TryGetInt64(out long aValue));
        Assert.Equal(42, aValue);

        Assert.True(view.TryGetValue("B", out EventFieldValue b));
        Assert.Equal("hello", b.AsString());

        Assert.False(view.TryGetValue("Missing", out _));
    }

    [Fact]
    public void TryGetValue_NumericPath_DoesNotAllocate()
    {
        TemplateFieldSchema schema = Schema("<template><data name=\"A\"/></template>");
        var values = ImmutableArray.Create((EventProperty)123);
        var view = new EventDataView(values, schema);

        // Warm the lazy name-index map and JIT before measuring.
        for (int i = 0; i < 100; i++)
        {
            view.TryGetValue("A", out EventFieldValue warm);
            warm.TryGetInt64(out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            view.TryGetValue("A", out EventFieldValue value);
            value.TryGetInt64(out _);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }

    private static TemplateFieldSchema Schema(string template) => new TemplateAnalyzer().GetTemplateInfo(template).Schema;
}

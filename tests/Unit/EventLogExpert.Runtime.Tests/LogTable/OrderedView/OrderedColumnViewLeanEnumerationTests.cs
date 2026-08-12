// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedColumnViewLeanEnumerationTests
{
    [Fact]
    public void EnumerateDetailLean_PreservesGridFieldsInDisplayOrder_ButOmitsDetailFields()
    {
        const int count = 64;
        ResolvedEvent[] events = BuildDetailRichEvents(count);

        OrderedColumnView view = BuildView(events, new SortContext(ColumnName.EventId, isDescending: true, groupBy: null, isGroupDescending: false));

        List<ResolvedEvent> full = [.. view.EnumerateDetail()];
        List<ResolvedEvent> lean = [.. view.EnumerateDetailLean()];

        long?[] expectedByDisplay = [.. Enumerable.Range(1, count).Reverse().Select(recordId => (long?)recordId)];
        Assert.Equal(expectedByDisplay, full.Select(@event => @event.RecordId));
        Assert.Equal(expectedByDisplay, lean.Select(@event => @event.RecordId));

        Assert.All(full, @event =>
        {
            Assert.NotEmpty(@event.Xml);
            Assert.True(@event.EventData.Count > 0);
            Assert.False(@event.UserData.IsDefaultOrEmpty);
        });

        for (int i = 0; i < lean.Count; i++)
        {
            ResolvedEvent expected = full[i];
            ResolvedEvent actual = lean[i];

            Assert.Equal(string.Empty, actual.Xml);
            Assert.Equal(0, actual.EventData.Count);
            Assert.True(actual.UserData.IsDefaultOrEmpty);

            Assert.Equal(expected.RecordId, actual.RecordId);
            Assert.Equal(expected.TimeCreated, actual.TimeCreated);
            Assert.Equal(expected.Level, actual.Level);
            Assert.Equal(expected.Source, actual.Source);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.TaskCategory, actual.TaskCategory);
            Assert.Equal(expected.ComputerName, actual.ComputerName);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.KeywordsDisplayName, actual.KeywordsDisplayName);
            Assert.Equal(expected.ProcessId, actual.ProcessId);
            Assert.Equal(expected.ThreadId, actual.ThreadId);
            Assert.Equal(expected.ActivityId, actual.ActivityId);
            Assert.Equal(expected.OwningLog, actual.OwningLog);
            Assert.Equal(expected.UserId, actual.UserId);
        }
    }

    private static ResolvedEvent[] BuildDetailRichEvents(int count)
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string[] keywords = ["Audit Success"];
        var activityId = new Guid("11112222-3333-4444-5555-666677778888");
        var userId = new SecurityIdentifier("S-1-5-21-1004336348-1177238915-682003330-512");
        var events = new ResolvedEvent[count];

        for (int i = 0; i < count; i++)
        {
            events[i] = new ResolvedEvent("Application", LogPathType.Channel)
            {
                RecordId = i + 1,
                TimeCreated = baseTime.AddSeconds(i),
                Level = i % 2 == 0 ? "Information" : "Error",
                Source = "Microsoft-Windows-Servicing",
                Id = 1000 + i,
                TaskCategory = "None",
                ComputerName = "DESKTOP-AB12CDE",
                Keywords = keywords,
                ProcessId = 4000 + i,
                ThreadId = 8000 + i,
                ActivityId = activityId,
                UserId = userId,
                Description = $"Row {i} entered the running state for package KB{5000000 + i}.",
                Xml =
                    $"<Event><System><EventID>{1000 + i}</EventID></System>" +
                    $"<EventData><Data Name='PackageIdentifier'>KB{5000000 + i}</Data></EventData></Event>"
            }.WithEventData(
                ("PackageIdentifier", $"KB{5000000 + i}"),
                ("CurrentState", "Installed"),
                ("ErrorCode", (long)i))
             .WithUserData(
                ("CbsPackageChangeState/ErrorCode", $"0x{i:X8}"));
        }

        return events;
    }

    private static OrderedColumnView BuildView(ResolvedEvent[] events, SortContext context)
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        var state = new OrderedViewState();
        state.ReconcileLog(logId, reader);
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request)));

        return new OrderedColumnView(state.Current, reader);
    }
}

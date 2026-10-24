// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class FilterPaneCommandsTests
{
    [Fact]
    public void ExcludeEventsAfter_DispatchesAnOpenEndedBeforeBoundary()
    {
        var timeCreatedUtc = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterPaneCommands(dispatcher).ExcludeEventsAfter(timeCreatedUtc);

        dispatcher.Received(1).Dispatch(Arg.Is<NarrowFilterDateRangeAction>(action =>
            action != null && action.After == null && action.Before == timeCreatedUtc));
    }

    [Fact]
    public void ExcludeEventsBefore_DispatchesAnOpenEndedAfterBoundary()
    {
        var timeCreatedUtc = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterPaneCommands(dispatcher).ExcludeEventsBefore(timeCreatedUtc);

        dispatcher.Received(1).Dispatch(Arg.Is<NarrowFilterDateRangeAction>(action =>
            action != null && action.After == timeCreatedUtc && action.Before == null));
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.UI.FilterEditor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class FilterRowOrderingTests : BunitContext
{
    [Fact]
    public void FilterRow_RepaintsWhenTheLibraryEntriesSourceChanges()
    {
        var source = Substitute.For<ILibraryEntriesSource>();
        source.Current.Returns(ImmutableList<LibraryEntry>.Empty);
        Services.AddSingleton(source);
        Services.AddSingleton(Substitute.For<IFilterPaneCommands>());
        Services.AddSingleton(Substitute.For<IAlertDialogService>());
        Services.AddSingleton(Substitute.For<IAnnouncementService>());
        Services.AddSingleton(Substitute.For<IEventLogQueries>());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<FilterRow>(parameters => parameters.Add(p => p.Value, SavedFilter.TryCreate("Level == 4")!));
        var rendersBefore = cut.RenderCount;

        source.Current.Returns(ImmutableList.Create<LibraryEntry>(
            new LibraryEntryFilterSet { Name = "set", CreatedUtc = DateTimeOffset.UnixEpoch, Filters = [] }));
        cut.InvokeAsync(() => source.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.True(cut.RenderCount > rendersBefore));
    }
}

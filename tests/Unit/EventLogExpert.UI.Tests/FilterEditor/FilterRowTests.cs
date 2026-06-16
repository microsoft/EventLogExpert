// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.UI.FilterEditor;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class FilterRowTests : BunitContext
{
    private readonly IAlertDialogService _alerts = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterPaneCommands _filterPaneCommands = Substitute.For<IFilterPaneCommands>();
    private readonly IState<FilterLibraryState> _libraryState = Substitute.For<IState<FilterLibraryState>>();

    public FilterRowTests()
    {
        _libraryState.Value.Returns(new FilterLibraryState());

        Services.AddSingleton(_alerts);
        Services.AddSingleton(_announcements);
        Services.AddSingleton(_filterPaneCommands);
        Services.AddSingleton(_libraryState);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void SavedRow_DoesNotWireEditOrCancelOnFilterEditorCore()
    {
        var saved = BuildSavedFilter("Level == 4");

        var component = Render<FilterRow>(p => p.Add(x => x.Value, saved));

        var core = component.FindComponent<FilterEditorCore>();

        Assert.False(
            core.Instance.OnEdit.HasDelegate,
            "FilterRow must not wire FilterEditorCore.OnEdit (pane Edit render bug; keep parity with the library path).");
        Assert.False(
            core.Instance.OnCancel.HasDelegate,
            "FilterRow must not wire FilterEditorCore.OnCancel (pane Cancel render bug; keep parity with the library path).");
    }

    private static SavedFilter BuildSavedFilter(string text) =>
        new()
        {
            ComparisonText = text,
            Compiled = null,
            IsEnabled = true,
        };
}

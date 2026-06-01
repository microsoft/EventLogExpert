// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class FilterLibraryModalTests : BunitContext
{
    private readonly IFilterLibraryCommands _commands = Substitute.For<IFilterLibraryCommands>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ModalId _modalId = new(1L);
    private readonly IModalService _modalService = Substitute.For<IModalService>();

    public FilterLibraryModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        _modalService.ActiveModalId.Returns(_modalId);

        Services.AddSingleton(_commands);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(FilterLibraryModal).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task Apply_DispatchesApplyEntryAndCompletesModalWithTrue()
    {
        // Arrange
        var entry = BuildFilterEntry("First");
        SetState(new FilterLibraryState { Entries = [entry], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        // Act
        await component.Find(".library-entry-row button.button-green").ClickAsync(new MouseEventArgs());

        // Assert
        _commands.Received(1).ApplyEntry(entry.Id);
        _modalService.Received(1).Complete(_modalId, Arg.Is<object?>(value => Equals(value, true)));
    }

    [Fact]
    public void Render_WhenEntriesIsEmpty_RendersEmptyState()
    {
        // Arrange — default FilterLibraryState has Entries empty, IsLoaded=false, LoadError=false.
        SetState(new FilterLibraryState { IsLoaded = true });

        // Act
        var component = Render<FilterLibraryModal>();

        // Assert
        Assert.NotNull(component.Find(".filter-library-empty"));
    }

    [Fact]
    public void Render_WhenLoadError_RendersErrorState()
    {
        // Arrange
        SetState(new FilterLibraryState { LoadError = true, IsLoaded = true });

        // Act
        var component = Render<FilterLibraryModal>();

        // Assert
        Assert.NotNull(component.Find(".filter-library-error"));
    }

    [Fact]
    public void Render_WhenNotLoaded_RendersLoadingStateNotEmptyState()
    {
        SetState(new FilterLibraryState { IsLoaded = false, IsLoading = true });

        var component = Render<FilterLibraryModal>();

        Assert.NotNull(component.Find(".filter-library-loading"));
        Assert.Empty(component.FindAll(".filter-library-empty"));
    }

    [Fact]
    public void Render_WithEntries_RendersOneRowPerEntry()
    {
        // Arrange
        var e1 = BuildFilterEntry("First");
        var e2 = BuildFilterEntry("Second");
        SetState(new FilterLibraryState { Entries = [e1, e2], IsLoaded = true });

        // Act
        var component = Render<FilterLibraryModal>();

        // Assert
        Assert.Equal(2, component.FindAll(".library-entry-row").Count);
    }

    private static LibraryEntrySavedFilter BuildFilterEntry(string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }

    private void SetState(FilterLibraryState state)
    {
        var stateMock = Substitute.For<IState<FilterLibraryState>>();
        stateMock.Value.Returns(state);
        Services.AddSingleton(stateMock);
    }
}

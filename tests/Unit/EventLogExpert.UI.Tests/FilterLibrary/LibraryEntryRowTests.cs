// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.FilterLibrary;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class LibraryEntryRowTests : BunitContext
{
    [Fact]
    public async Task Apply_InvokesOnApplyWithEntryId()
    {
        // Arrange
        LibraryEntryId? captured = null;
        var entry = BuildFilterEntry("Test");
        var component = Render<LibraryEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnApply, id => { captured = id; return Task.CompletedTask; }));

        // Act
        await component.Find("button.button-green").ClickAsync(new MouseEventArgs());

        // Assert
        Assert.Equal(entry.Id, captured);
    }

    [Fact]
    public async Task Delete_InvokesOnDeleteWithEntryId()
    {
        // Arrange
        LibraryEntryId? captured = null;
        var entry = BuildFilterEntry("Test");
        var component = Render<LibraryEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnDelete, id => { captured = id; return Task.CompletedTask; }));

        // Act
        await component.Find("button.button-red").ClickAsync(new MouseEventArgs());

        // Assert
        Assert.Equal(entry.Id, captured);
    }

    [Fact]
    public void Render_DoesNotRenderEditButton_ForPR2()
    {
        // PR-2 deliberately ships without an Edit button (regression guard against accidentally adding one back).
        var component = Render<LibraryEntryRow>(parameters => parameters
            .Add(p => p.Entry, BuildFilterEntry("Test")));

        Assert.DoesNotContain(component.FindAll("button"), b => b.TextContent.Trim() == "Edit");
    }

    [Fact]
    public void Render_PresetEntry_DisplaysPresetBadge()
    {
        // Arrange
        var f1 = SavedFilter.TryCreate("Level == 2");
        Assert.NotNull(f1);
        var preset = new LibraryEntryPreset
        {
            Name = "Test",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [f1],
        };

        // Act
        var component = Render<LibraryEntryRow>(parameters => parameters
            .Add(p => p.Entry, preset));

        // Assert
        Assert.Equal("Preset", component.Find(".library-entry-kind-badge").TextContent.Trim());
    }

    [Fact]
    public void Render_SavedFilterEntry_DisplaysFilterBadge()
    {
        // Arrange + Act
        var component = Render<LibraryEntryRow>(parameters => parameters
            .Add(p => p.Entry, BuildFilterEntry("Test")));

        // Assert
        Assert.Equal("Filter", component.Find(".library-entry-kind-badge").TextContent.Trim());
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
}

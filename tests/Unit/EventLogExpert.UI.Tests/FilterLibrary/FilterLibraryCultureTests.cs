// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.FilterLibrary;

[Collection(CultureSensitiveCollection.Name)]
public sealed class FilterLibraryCultureTests : BunitContext
{
    private readonly ILibraryEntriesSource _libraryEntries = Substitute.For<ILibraryEntriesSource>();
    private readonly ILibraryLoadStatusSource _loadStatus = Substitute.For<ILibraryLoadStatusSource>();

    public FilterLibraryCultureTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();
        Services.AddSingleton(Substitute.For<IAlertDialogService>());
        Services.AddSingleton(Substitute.For<IAnnouncementService>());
        Services.AddSingleton(Substitute.For<IClipboardService>());
        Services.AddSingleton(Substitute.For<IFilterLibraryCommands>());
        Services.AddSingleton(Substitute.For<IFilterLibraryExportService>());
        Services.AddSingleton(Substitute.For<IFilePickerService>());
        Services.AddSingleton(_libraryEntries);
        Services.AddSingleton(_loadStatus);
        Services.AddSingleton(Substitute.For<IModalCoordinator>());
        Services.AddSingleton(Substitute.For<IModalService>());
        Services.AddSingleton(Substitute.For<ITagBulkUpdateFailedNotifier>());
        Services.AddSingleton(Substitute.For<IScenarioAuthoringService>());
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddSingleton(new ScenarioAuthoringOptions(false));

        var activeFilters = Substitute.For<IActiveFiltersSource>();
        activeFilters.Current.Returns(ImmutableList<SavedFilter>.Empty);
        Services.AddSingleton(activeFilters);

        _loadStatus.Current.Returns(new LibraryLoadStatus(false, false));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LibraryEntryRow_LastUsedDateUsesCurrentCultureShortDate()
    {
        var lastUsed = new DateTimeOffset(2026, 8, 26, 17, 57, 5, TimeSpan.Zero);
        LibraryEntrySavedFilter entry = BuildAutoTrackedFilterEntry("Recent") with { LastUsedUtc = lastUsed };

        string english = RenderEntryName(CultureInfo.GetCultureInfo("en-US"), entry);
        string finnish = RenderEntryName(CultureInfo.GetCultureInfo("fi-FI"), entry);

        Assert.Contains(lastUsed.ToLocalTime().ToString("d", CultureInfo.GetCultureInfo("en-US")), english, StringComparison.Ordinal);
        Assert.Contains(lastUsed.ToLocalTime().ToString("d", CultureInfo.GetCultureInfo("fi-FI")), finnish, StringComparison.Ordinal);
        Assert.NotEqual(english, finnish);
    }

    [Fact]
    public void ModalTabCounts_GroupUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        ImmutableList<LibraryEntry> entries = Enumerable.Range(0, 1234)
            .Select(index => BuildSavedFilter(string.Create(CultureInfo.InvariantCulture, $"Saved {index}")))
            .Cast<LibraryEntry>()
            .ToImmutableList();
        _libraryEntries.Current.Returns(new FilterLibraryState { Entries = entries, IsLoaded = true }.Entries);
        _loadStatus.Current.Returns(new LibraryLoadStatus(true, false));

        IRenderedComponent<FilterLibraryModal> component = RunUnderCulture(culture, () => Render<FilterLibraryModal>());

        Assert.Contains(
            component.FindAll("button").Select(button => button.TextContent),
            text => text.Contains(1234.ToString("N0", culture), StringComparison.Ordinal));
    }

    private static LibraryEntrySavedFilter BuildAutoTrackedFilterEntry(string name) =>
        BuildFilterEntry(name) with
        {
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = DateTimeOffset.UtcNow.AddDays(-8)
        };

    private static LibraryEntrySavedFilter BuildFilterEntry(string name)
    {
        SavedFilter? filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }

    private static LibraryEntrySavedFilter BuildSavedFilter(string name) =>
        BuildFilterEntry(name) with { Origin = LibraryEntryOrigin.UserSaved };

    private static T RunUnderCulture<T>(CultureInfo culture, Func<T> build)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            return build();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    private string RenderEntryName(CultureInfo culture, LibraryEntrySavedFilter entry)
    {
        IRenderedComponent<LibraryEntryRow> component = RunUnderCulture(culture, () => Render<LibraryEntryRow>(parameters => parameters
            .Add(row => row.Entry, entry)
            .Add(row => row.ActiveTab, LibraryTab.PreviouslyUsed)
            .Add(row => row.AllFilterSets, [])
            .Add(row => row.OnAddToFilterSet, _ => Task.CompletedTask)
            .Add(row => row.OnApply, _ => Task.CompletedTask)
            .Add(row => row.OnDelete, _ => Task.CompletedTask)
            .Add(row => row.OnReplace, _ => Task.CompletedTask)
            .Add(row => row.OnRequestPendingFocus, _ => Task.CompletedTask)
            .Add(row => row.OnSaveToLibrary, _ => Task.CompletedTask)
            .Add(row => row.OnToggleFavorite, _ => Task.CompletedTask)));
        return component.Find(".library-entry-name").TextContent;
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.UI.FilterEditor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Reflection;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class FilterEditorCoreTests : BunitContext
{
    private readonly IAlertDialogService _alerts = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();

    public FilterEditorCoreTests()
    {
        Services.AddSingleton(_alerts);
        Services.AddSingleton(_announcements);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AvailableTags_ReturnsDistinctSortedUnion()
    {
        var options = new List<CachedFilterOption>
        {
            new("a", true, ["zebra", "alpha"]),
            new("b", false, ["alpha", "mid"]),
        };

        var tags = FilterEditorCore.AvailableTags(options);

        Assert.Equal(new[] { "alpha", "mid", "zebra" }, tags.ToArray());
    }

    [Fact]
    public void CachedMode_EmptyCachedOptions_RendersReadOnlyInputAndHint()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Level == 4" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, new List<CachedFilterOption>()));

        Assert.NotNull(component.Find(".filter-row-cached input[readonly]"));
        Assert.NotNull(component.Find(".filter-row-hint"));
    }

    [Fact]
    public void CachedMode_NullCachedOptions_RendersReadOnlyInputAndHint()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Level == 4" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, null));

        var readOnlyInput = component.Find(".filter-row-cached input[readonly]");
        Assert.Equal("Level == 4", readOnlyInput.GetAttribute("value"));

        var hint = component.Find(".filter-row-hint");
        Assert.Contains("No recent options", hint.TextContent);
    }

    [Fact]
    public void CachedMode_OptionsHaveNoTags_DoesNotRenderTagMultiSelect()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Level == 4" };
        var options = new List<CachedFilterOption> { new("Level == 4", true, []) };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, options));

        Assert.Empty(component.FindAll("[aria-label='Filter recent options by tag']"));
    }

    [Fact]
    public void CachedMode_OptionsHaveTags_RendersTagMultiSelect()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Level == 4" };
        var options = new List<CachedFilterOption> { new("Level == 4", true, ["network"]) };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, options));

        Assert.NotNull(component.Find("[aria-label='Filter recent options by tag']"));
    }

    [Fact]
    public void EditThenCancel_TogglesEditingStateAndAnnounces()
    {
        var saved = MakeSavedFilter("Id == 1000");

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.Value, saved)
            .Add(p => p.CachedOptions, new List<CachedFilterOption>()));

        Assert.False(component.Instance.IsEditing);

        component.Find("button[title='Edit filter']").Click();

        Assert.True(component.Instance.IsEditing);
        _announcements.Received(1).Announce("Editing filter");

        component.Find("button[aria-label='Cancel edit']").Click();

        Assert.False(component.Instance.IsEditing);
        _announcements.Received(1).Announce("Edit cancelled");
    }

    [Fact]
    public void FilterCachedByTags_AllSemantics_RequiresEverySelectedTag()
    {
        var options = new List<CachedFilterOption>
        {
            new("both", false, ["x", "y"]),
            new("onlyx", false, ["x"]),
        };

        var result = FilterEditorCore.FilterCachedByTags(options, ["x", "y"], currentSelection: null);

        Assert.Single(result);
        Assert.Equal("both", result[0].Value);
    }

    [Fact]
    public void FilterCachedByTags_NoSelectedTags_ReturnsAllOptions()
    {
        var options = new List<CachedFilterOption>
        {
            new("a", true, ["x"]),
            new("b", false, []),
        };

        var result = FilterEditorCore.FilterCachedByTags(options, [], currentSelection: null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterCachedByTags_PreservesCurrentSelectionEvenWhenItDoesNotMatch()
    {
        var options = new List<CachedFilterOption>
        {
            new("tagged", false, ["x"]),
            new("current", false, ["other"]),
        };

        var result = FilterEditorCore.FilterCachedByTags(options, ["x"], currentSelection: "current");

        Assert.Contains(result, o => o.Value == "current");
        Assert.Contains(result, o => o.Value == "tagged");
    }

    [Fact]
    public async ValueTask FocusEditAsync_NoShellRef_ReturnsCompleted()
    {
        var core = new FilterEditorCore();

        await core.FocusEditAsync();
    }

    [Fact]
    public void GetAvailableModes_CachedOptionsBecomePopulated_ShowsRecentAfterRerender()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 1" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, new List<CachedFilterOption>()));

        var initialItems = component.Find(".filter-row-mode .dropdown-list").QuerySelectorAll("[role='option']");
        Assert.DoesNotContain(initialItems, item => item.TextContent.Contains("Recent"));

        component.Render(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, new List<CachedFilterOption> { new("Level == 4", IsFavorite: true, Tags: []) }));

        var updatedItems = component.Find(".filter-row-mode .dropdown-list").QuerySelectorAll("[role='option']");
        Assert.Contains(updatedItems, item => item.TextContent.Contains("Recent"));
    }

    [Fact]
    public void GetAvailableModes_CachedOptionsEmpty_FilterIsCached_ShowsCachedInDropdown()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Id == 1" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, new List<CachedFilterOption>()));

        var modeListbox = component.Find(".filter-row-mode .dropdown-list");
        var items = modeListbox.QuerySelectorAll("[role='option']");

        Assert.Contains(items, item => item.TextContent.Contains("Recent"));
    }

    [Fact]
    public void GetAvailableModes_CachedOptionsEmpty_FilterNotCached_HidesCachedFromDropdown()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 1" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, new List<CachedFilterOption>()));

        var modeListbox = component.Find(".filter-row-mode .dropdown-list");
        var items = modeListbox.QuerySelectorAll("[role='option']");

        Assert.DoesNotContain(items, item => item.TextContent.Contains("Recent"));
    }

    [Fact]
    public void GetAvailableModes_CachedOptionsNonNull_ShowsAllModes()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 1" };
        var options = new List<CachedFilterOption>
        {
            new("Level == 4", IsFavorite: true, Tags: []),
        };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, options));

        var modeListbox = component.Find(".filter-row-mode .dropdown-list");
        var items = modeListbox.QuerySelectorAll("[role='option']");

        Assert.Contains(items, item => item.TextContent.Contains("Basic"));
        Assert.Contains(items, item => item.TextContent.Contains("Advanced"));
        Assert.Contains(items, item => item.TextContent.Contains("Recent"));
    }

    [Fact]
    public void GetAvailableModes_CachedOptionsNull_FilterIsCached_ShowsCachedInDropdown()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Id == 1" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, null));

        var modeListbox = component.Find(".filter-row-mode .dropdown-list");
        var items = modeListbox.QuerySelectorAll("[role='option']");

        Assert.Contains(items, item => item.TextContent.Contains("Recent"));
    }

    [Fact]
    public void GetAvailableModes_CachedOptionsNull_FilterNotCached_HidesCachedFromDropdown()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 1" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, null));

        var modeListbox = component.Find(".filter-row-mode .dropdown-list");
        var items = modeListbox.QuerySelectorAll("[role='option']");

        Assert.DoesNotContain(items, item => item.TextContent.Contains("Recent"));
    }

    [Fact]
    public void IsEditing_NoDraft_ReturnsFalse()
    {
        var saved = MakeSavedFilter("Id == 1000");

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.Value, saved));

        Assert.False(component.Instance.IsEditing);
    }

    [Fact]
    public void IsEditing_WithPendingDraft_ReturnsTrue()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 1" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft));

        Assert.True(component.Instance.IsEditing);
    }

    [Fact]
    public void OnParametersSet_BothValueAndPendingDraft_DropsDraftPendingToSavedTransition()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 1" };
        var saved = MakeSavedFilter("Id == 1");

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft));

        Assert.True(component.Instance.IsEditing);

        component.Render(parameters => parameters
            .Add(p => p.Value, saved)
            .Add(p => p.PendingDraft, draft));

        Assert.False(component.Instance.IsEditing);
    }

    [Fact]
    public void OnParametersSet_BothValueAndPendingDraftNull_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Render<FilterEditorCore>(parameters => parameters
                .Add(p => p.Value, null)
                .Add(p => p.PendingDraft, null)));

        Assert.Contains("requires either", ex.Message);
        Assert.Contains("Value", ex.Message);
        Assert.Contains("PendingDraft", ex.Message);
    }

    [Fact]
    public void OnParametersSet_OnlyPendingDraft_AdoptsAsFilterAndIsEditing()
    {
        var draft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 1" };

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft));

        Assert.True(component.Instance.IsEditing);
    }

    [Fact]
    public void OnParametersSet_OnlyValue_RendersSavedView()
    {
        var saved = MakeSavedFilter("Id == 1000");

        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.Value, saved));

        Assert.False(component.Instance.IsEditing);
    }

    [Fact]
    public void OnParametersSet_PrunesSelectedTagsNoLongerAvailable()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Level == 4" };
        var component = Render<FilterEditorCore>(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, new List<CachedFilterOption> { new("Level == 4", true, ["alpha", "beta"]) }));

        var tagsField = (List<string>)typeof(FilterEditorCore)
            .GetField("_selectedTags", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component.Instance)!;
        tagsField.Add("alpha");
        tagsField.Add("beta");

        component.Render(parameters => parameters
            .Add(p => p.PendingDraft, draft)
            .Add(p => p.CachedOptions, new List<CachedFilterOption> { new("Level == 4", true, ["alpha"]) }));

        Assert.Contains("alpha", tagsField);
        Assert.DoesNotContain("beta", tagsField);
    }

    private static SavedFilter MakeSavedFilter(string text) =>
        new()
        {
            ComparisonText = text,
            Compiled = null,
            IsEnabled = true,
        };
}

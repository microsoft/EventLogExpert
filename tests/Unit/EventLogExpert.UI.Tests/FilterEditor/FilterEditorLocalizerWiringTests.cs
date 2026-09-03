// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.FilterEditor.Comparison;
using EventLogExpert.UI.FilterEditor.Editing;
using EventLogExpert.UI.FilterEditor.Rows;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;
using System.Reflection;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class FilterEditorLocalizerWiringTests : BunitContext
{
    private readonly IAlertDialogService _alerts = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();

    public FilterEditorLocalizerWiringTests()
    {
        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(new EventLogState());
        Services.AddSingleton(eventLogState);

        var eventLogQueries = Substitute.For<IEventLogQueries>();
        eventLogQueries.GetPropertyValues(default).ReturnsForAnyArgs(ImmutableArray<string>.Empty);
        eventLogQueries.GetEventDataFieldNames().Returns(ImmutableArray<string>.Empty);
        eventLogQueries.GetUserDataFieldNames().Returns(ImmutableArray<string>.Empty);
        eventLogQueries.GetEventDataFieldValues(default!).ReturnsForAnyArgs(ImmutableArray<string>.Empty);
        eventLogQueries.GetUserDataFieldValues(default!).ReturnsForAnyArgs(ImmutableArray<string>.Empty);
        Services.AddSingleton(eventLogQueries);

        Services.AddSingleton(_alerts);
        Services.AddSingleton(_announcements);
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddFluxor(options => options.ScanAssemblies(typeof(FilterComparisonEditor).Assembly));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void CoreAdvancedExpressionAndCachedEmptyStates_RouteStateSpecificMarkers()
    {
        var include = RenderCore(new FilterDraft { Mode = FilterMode.Advanced, IsExcluded = false });
        Assert.Equal(
            "[[FilterEditor_AdvancedExpression_IncludeAria]]",
            include.Find("input.advanced-filter").GetAttribute("aria-label"));

        var exclude = RenderCore(new FilterDraft { Mode = FilterMode.Advanced, IsExcluded = true });
        Assert.Equal(
            "[[FilterEditor_AdvancedExpression_ExcludeAria]]",
            exclude.Find("input.advanced-filter").GetAttribute("aria-label"));

        var noOptions = RenderCore(new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Level == 4" }, cachedOptions: []);
        Assert.Equal(
            "[[FilterEditor_Recent_ReadOnlyAria]]",
            noOptions.Find(".filter-row-cached input[readonly]").GetAttribute("aria-label"));

        var noMatches = RenderCore(
            new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Id == 1" },
            cachedOptions:
            [
                new CachedFilterOption("Level == 4", false, ["network"]),
                new CachedFilterOption("Source == x", false, ["database"])
            ]);
        SelectedTags(noMatches).AddRange(["network", "database"]);
        noMatches.Render();

        Assert.Equal("[[FilterEditor_Recent_EmptyNoMatches]]", noMatches.Find(".filter-row-hint").TextContent);
    }

    [Fact]
    public void CoreAndRows_RouteChromeThroughLocalizerAndKeepDataVerbatim()
    {
        var draft = new FilterDraft { Mode = FilterMode.Cached, ComparisonText = "Level == 4" };
        var component = RenderCore(
            draft,
            cachedOptions: [new CachedFilterOption("Level == 4", true, ["network"])]);

        Assert.Contains("[[FilterEditor_ModeLabel]]", component.Markup);
        Assert.Contains("[[FilterEditor_Recent_TagFilterAria]]", component.Markup);
        Assert.Contains("[[FilterEditor_Recent_AllTags]]", component.Markup);
        Assert.Contains("[[FilterEditor_Recent_SelectAria]]", component.Markup);
        Assert.Contains("[[FilterEditor_Recent_FavoriteAria]]", component.Markup);

        var tagOption = component.FindAll("[role='option']")
            .Single(option => option.TextContent.Trim() == "network");
        Assert.Equal("network", tagOption.TextContent.Trim());
        Assert.Equal("Level == 4", component.Find(".cached-option-text").TextContent);
        Assert.DoesNotContain("[[network]]", component.Markup);
        Assert.DoesNotContain("[[Level == 4]]", component.Markup);
    }

    [Fact]
    public async Task CoreAnnouncements_RouteEveryHandlerThroughMarkerLocalizer()
    {
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Advanced }), "CancelHandler");
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_FilterDiscarded]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Advanced }), "RemoveHandler");
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_FilterDiscarded]]");

        _announcements.ClearReceivedCalls();
        var savedCore = RenderCore(value: MakeSavedFilter("Id == 1"));
        await InvokeAsync(savedCore, "EditHandler");
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_EditingFilter]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(savedCore, "CancelHandler");
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_EditCancelled]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(value: MakeSavedFilter("Id == 1")), "ExclusionHandler", true);
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_SetToExclude]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(value: MakeSavedFilter("Id == 1")), "ExclusionHandler", false);
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_SetToInclude]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Advanced }), "ExclusionHandler", true);
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_SetToExclude]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Advanced }), "ExclusionHandler", false);
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_SetToInclude]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(value: MakeSavedFilter("Id == 1")), "RemoveHandler");
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_FilterRemoved]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id == 1" }), "SaveHandler");
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_FilterSaved]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(value: MakeSavedFilter("Id == 1", isEnabled: true)), "ToggleEnabledHandler");
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_FilterDisabled]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(value: MakeSavedFilter("Id == 1", isEnabled: false)), "ToggleEnabledHandler");
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_FilterEnabled]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Basic, Comparison = { Value = "1" } }), "TryChangeModeAsync", FilterMode.Advanced);
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_SwitchedToAdvanced]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Advanced }), "TryChangeModeAsync", FilterMode.Basic);
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_SwitchedToBasic]]");

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Cached }), "TryChangeModeAsync", FilterMode.Cached);
        _announcements.DidNotReceive().Announce(Arg.Any<string>());

        _announcements.ClearReceivedCalls();
        await InvokeAsync(RenderCore(new FilterDraft { Mode = FilterMode.Advanced }), "TryChangeModeAsync", FilterMode.Cached);
        _announcements.Received(1).Announce("[[FilterEditor_Announcement_SwitchedToRecent]]");
    }

    [Fact]
    public async Task CoreModeSwitchDialog_UsesLocalizedMarkersForLossySwitch()
    {
        _alerts.ShowAlert(default!, default!, default!, (string)default!).ReturnsForAnyArgs(true);
        var component = RenderCore(new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Id ===== ###" });

        await InvokeAsync(component, "TryChangeModeAsync", FilterMode.Basic);

        await _alerts.Received().ShowAlert(
            "[[FilterEditor_ModeSwitch_Title]]",
            "[[FilterEditor_ModeSwitch_Message_ToBasic]]",
            "[[FilterEditor_Action_Continue]]",
            "[[Modal_Cancel]]");
    }

    [Fact]
    public async Task CoreSaveErrors_LocalizeTypedFailuresButPreserveCompilerDiagnostics()
    {
        var empty = RenderCore(new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = string.Empty });
        await InvokeAsync(empty, "SaveHandler");
        Assert.Contains("[[FilterEditor_SaveError_EmptyFilter]]", empty.Markup);

        var incomplete = RenderCore(new FilterDraft { Mode = FilterMode.Basic, Predicates = [new FilterPredicateDraft()] });
        await InvokeAsync(incomplete, "SaveHandler");
        Assert.Contains("[[FilterEditor_SaveError_IncompletePredicates]]", incomplete.Markup);

        var diagnosticDraft = new FilterDraft { Mode = FilterMode.Advanced, ComparisonText = "Source == \"unterminated" };
        Assert.False(diagnosticDraft.TryBuildSavedFilter(out _, out var failure));
        var expectedDiagnostic = Assert.IsType<FilterDraftBuildFailure.CompilerDiagnostic>(failure).Message;

        var diagnostic = RenderCore(diagnosticDraft);
        await InvokeAsync(diagnostic, "SaveHandler");
        var error = diagnostic.Find(".advanced-filter-error");
        Assert.Equal(expectedDiagnostic, error.TextContent);
        Assert.DoesNotContain("[[", error.TextContent);
    }

    [Fact]
    public void FilterEditPanel_RoutesActionsAndHighlightColorsThroughLocalizer()
    {
        var component = Render<FilterEditPanel>(parameters => parameters
            .Add(panel => panel.Filter, new FilterDraft { Mode = FilterMode.Advanced, Color = HighlightColor.LightRed }));

        Assert.Contains("[[FilterEditor_EditPanel_IncludedAria]]", component.Markup);
        Assert.Contains("[[FilterEditor_ExcludeToggle_TitleIncluded]]", component.Markup);
        Assert.Contains("[[FilterEditor_HighlightColor_Aria]]", component.Markup);
        Assert.Contains("[[FilterEditor_HighlightColorOption_LightRed]]", component.Markup);
        Assert.Contains("data-highlight=\"lightred\"", component.Markup);
        Assert.Contains("[[FilterEditor_Action_Save]]", component.Markup);
        Assert.Contains("[[FilterEditor_Action_CancelEdit_Aria]]", component.Markup);
        Assert.Equal(
            "[[FilterEditor_Action_CancelEdit_Title]]",
            component.Find("button[aria-label='[[FilterEditor_Action_CancelEdit_Aria]]']").GetAttribute("title"));
        Assert.Contains("[[FilterEditor_Action_RemoveFilter_Aria]]", component.Markup);
        Assert.Equal(
            "[[FilterEditor_Action_RemoveFilter_Title]]",
            component.Find("button[aria-label='[[FilterEditor_Action_RemoveFilter_Aria]]']").GetAttribute("title"));

        var excluded = Render<FilterEditPanel>(parameters => parameters
            .Add(panel => panel.Filter, new FilterDraft { Mode = FilterMode.Advanced, IsExcluded = true }));

        Assert.Contains("[[FilterEditor_EditPanel_ExcludedAria]]", excluded.Markup);
        Assert.Contains("[[FilterEditor_ExcludeToggle_TitleExcluded]]", excluded.Markup);
        Assert.Contains("[[FilterEditor_HighlightColor_IncludeOnlyDescription]]", excluded.Markup);
    }

    [Fact]
    public void FilterEditorAnnouncements_RouteEveryBranch()
    {
        MarkerLocalizer localizer = new();

        Assert.Equal("[[FilterEditor_Announcement_FilterDiscarded]]", FilterEditorAnnouncements.FilterDiscarded(localizer));
        Assert.Equal("[[FilterEditor_Announcement_EditCancelled]]", FilterEditorAnnouncements.EditCancelled(localizer));
        Assert.Equal("[[FilterEditor_Announcement_EditingFilter]]", FilterEditorAnnouncements.EditingFilter(localizer));
        Assert.Equal("[[FilterEditor_Announcement_SetToExclude]]", FilterEditorAnnouncements.FilterSetTo(localizer, true));
        Assert.Equal("[[FilterEditor_Announcement_SetToInclude]]", FilterEditorAnnouncements.FilterSetTo(localizer, false));
        Assert.Equal("[[FilterEditor_Announcement_FilterRemoved]]", FilterEditorAnnouncements.FilterRemoved(localizer));
        Assert.Equal("[[FilterEditor_Announcement_FilterSaved]]", FilterEditorAnnouncements.FilterSaved(localizer));
        Assert.Equal("[[FilterEditor_Announcement_FilterEnabled]]", FilterEditorAnnouncements.FilterEnabledState(localizer, true));
        Assert.Equal("[[FilterEditor_Announcement_FilterDisabled]]", FilterEditorAnnouncements.FilterEnabledState(localizer, false));
        Assert.Equal("[[FilterEditor_Announcement_SwitchedToAdvanced]]", FilterEditorAnnouncements.SwitchedToMode(localizer, FilterMode.Advanced));
        Assert.Equal("[[FilterEditor_Announcement_SwitchedToBasic]]", FilterEditorAnnouncements.SwitchedToMode(localizer, FilterMode.Basic));
        Assert.Equal("[[FilterEditor_Announcement_SwitchedToRecent]]", FilterEditorAnnouncements.SwitchedToMode(localizer, FilterMode.Cached));
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterEditorAnnouncements.SwitchedToMode(localizer, (FilterMode)999));
    }

    [Fact]
    public void FilterEditorHighlightColorLocalizer_RoutesEveryColorAndThrowsForUnknownColor()
    {
        MarkerLocalizer localizer = new();

        foreach (HighlightColor color in Enum.GetValues<HighlightColor>())
        {
            Assert.Equal($"[[FilterEditor_HighlightColorOption_{color}]]", FilterEditorHighlightColorLocalizer.Label(localizer, color));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FilterEditorHighlightColorLocalizer.Label(localizer, (HighlightColor)999));
    }

    [Theory]
    [InlineData(FilterMode.Advanced, "[[FilterEditor_Mode_Advanced]]")]
    [InlineData(FilterMode.Basic, "[[FilterEditor_Mode_Basic]]")]
    [InlineData(FilterMode.Cached, "[[FilterEditor_Mode_Recent]]")]
    public void FilterEditorModeLocalizer_Display_RoutesDefinedModes(FilterMode mode, string expected)
    {
        Assert.Equal(expected, FilterEditorModeLocalizer.Display(new MarkerLocalizer(), mode));
    }

    [Fact]
    public void FilterEditorModeLocalizer_Display_ThrowsForUnknownMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FilterEditorModeLocalizer.Display(new MarkerLocalizer(), (FilterMode)999));
    }

    [Fact]
    public void FilterEditorModeLocalizer_Map_CoversEveryModeWithCustomCachedMapping()
    {
        Dictionary<FilterMode, string> expected = new()
        {
            [FilterMode.Advanced] = "[[FilterEditor_Mode_Advanced]]",
            [FilterMode.Basic] = "[[FilterEditor_Mode_Basic]]",
            [FilterMode.Cached] = "[[FilterEditor_Mode_Recent]]"
        };

        Assert.Equal(Enum.GetValues<FilterMode>().Length, expected.Count);
        foreach ((FilterMode mode, string label) in expected)
        {
            Assert.Equal(label, FilterEditorModeLocalizer.Display(new MarkerLocalizer(), mode));
        }
    }

    [Fact]
    public void HighlightColorOptionResourceFamily_MatchesEnumMembers()
    {
        var keys = LoadSharedResourceData()
            .Where(element => element.Attribute("name")?.Value.StartsWith("FilterEditor_HighlightColorOption_", StringComparison.Ordinal) == true)
            .Select(element => element.Attribute("name")!.Value["FilterEditor_HighlightColorOption_".Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();

        var members = Enum.GetNames<HighlightColor>().Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(members, keys);
    }

    [Theory]
    [InlineData(FilterMode.Advanced, FilterMode.Cached, "[[FilterEditor_ModeSwitch_Message_ToRecent]]")]
    [InlineData(FilterMode.Basic, FilterMode.Cached, "[[FilterEditor_ModeSwitch_Message_ToRecent]]")]
    [InlineData(FilterMode.Cached, FilterMode.Cached, "[[FilterEditor_ModeSwitch_Message_ToRecent]]")]
    [InlineData(FilterMode.Advanced, FilterMode.Basic, "[[FilterEditor_ModeSwitch_Message_ToBasic]]")]
    [InlineData(FilterMode.Cached, FilterMode.Basic, "[[FilterEditor_ModeSwitch_Message_ToBasic]]")]
    [InlineData(FilterMode.Basic, FilterMode.Advanced, "[[FilterEditor_ModeSwitch_Message_ToAdvanced]]")]
    [InlineData(FilterMode.Cached, FilterMode.Advanced, "[[FilterEditor_ModeSwitch_Message_Generic]]")]
    [InlineData(FilterMode.Advanced, FilterMode.Advanced, "[[FilterEditor_ModeSwitch_Message_Generic]]")]
    [InlineData(FilterMode.Basic, FilterMode.Basic, "[[FilterEditor_ModeSwitch_Message_Generic]]")]
    public void ModeSwitchConfirmation_RoutesEveryDefinedTuple(FilterMode current, FilterMode target, string expected)
    {
        Assert.Equal(expected, FilterEditorModeSwitchLocalizer.ConfirmationMessage(new MarkerLocalizer(), current, target));
    }

    [Fact]
    public void ModeSwitchConfirmation_ThrowsForUnknownMode()
    {
        MarkerLocalizer localizer = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FilterEditorModeSwitchLocalizer.ConfirmationMessage(localizer, (FilterMode)999, FilterMode.Cached));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FilterEditorModeSwitchLocalizer.ConfirmationMessage(localizer, FilterMode.Advanced, (FilterMode)999));
    }

    [Fact]
    public void PredicateChip_RoutesEditAndRemoveThroughLocalizer()
    {
        var predicate = new FilterPredicateDraft { Comparison = { Value = "1" } };
        var component = RenderPredicate(predicate, isEditing: false);

        Assert.Contains("[[FilterEditor_Predicate_EditAria([[FilterEditor_PredicateSummary(Id|==|1)]])]]", component.Markup);
        Assert.Contains("[[FilterEditor_Predicate_EditTitle]]", component.Markup);
        Assert.Contains("[[FilterEditor_Predicate_RemoveAria([[FilterEditor_PredicateSummary(Id|==|1)]])]]", component.Markup);
        Assert.Contains("[[FilterEditor_Predicate_RemoveTitle]]", component.Markup);
    }

    [Fact]
    public void PredicateEditor_RoutesButtonsThroughLocalizer()
    {
        var predicate = new FilterPredicateDraft { JoinWithAny = true };
        var component = RenderPredicate(predicate, isEditing: true);

        Assert.Contains("[[FilterEditor_PredicateJoin_OrAria]]", component.Markup);
        Assert.Contains("[[FilterEditor_PredicateJoin_OrTitle]]", component.Markup);
        Assert.Contains("[[FilterEditor_PredicateJoin_OrLabel]]", component.Markup);
        Assert.Contains("[[FilterEditor_Predicate_DoneAria]]", component.Markup);
        Assert.Contains("[[FilterEditor_Predicate_DoneDisabledTitle]]", component.Markup);
        Assert.Contains("[[FilterEditor_Predicate_RemoveAria([[FilterEditor_PredicateSummary(Id|==|?)]])]]", component.Markup);
        Assert.Contains("[[FilterEditor_Predicate_RemoveTitle]]", component.Markup);

        var andPredicate = new FilterPredicateDraft { JoinWithAny = false };
        var andComponent = RenderPredicate(andPredicate, isEditing: true);

        Assert.Contains("[[FilterEditor_PredicateJoin_AndAria]]", andComponent.Markup);
        Assert.Contains("[[FilterEditor_PredicateJoin_AndTitle]]", andComponent.Markup);
        Assert.Contains("[[FilterEditor_PredicateJoin_AndLabel]]", andComponent.Markup);

        andPredicate.Comparison.Value = "1";
        andComponent.Render();
        Assert.Contains("[[FilterEditor_Predicate_DoneTitle]]", andComponent.Markup);
    }

    [Fact]
    public void PredicateList_RoutesAddButtonThroughLocalizer()
    {
        var enabled = Render<FilterPredicateList>(parameters => parameters.Add(list => list.Predicates, []));
        Assert.Contains("[[FilterEditor_PredicateList_AddAria]]", enabled.Markup);
        Assert.Contains("[[FilterEditor_PredicateList_AddTitle]]", enabled.Markup);

        var disabled = Render<FilterPredicateList>(parameters => parameters.Add(list => list.Predicates, [new FilterPredicateDraft()]));
        Assert.Contains("[[FilterEditor_PredicateList_AddDisabledAria]]", disabled.Markup);
        Assert.Contains("[[FilterEditor_PredicateList_AddDisabledTitle]]", disabled.Markup);
    }

    [Fact]
    public void PredicateSummaryResource_PreservesSpaces()
    {
        var data = LoadSharedResourceData()
            .Single(element => element.Attribute("name")?.Value == "FilterEditor_PredicateSummary");

        Assert.Equal("preserve", data.Attribute(XNamespace.Xml + "space")?.Value);
    }

    [Theory]
    [InlineData(0, "?")]
    [InlineData(1, "alpha")]
    [InlineData(2, "[[FilterEditor_PredicateSummary_ValueCount_Many(2)]]")]
    [InlineData(1500, "[[FilterEditor_PredicateSummary_ValueCount_Many(1500)]]")]
    public void PredicateSummary_ManyValueCounts_UseRequiredDisplayBranches(int count, string expectedValueLabel)
    {
        List<string> values = count switch
        {
            0 => [],
            1 => ["alpha"],
            _ => [.. Enumerable.Range(0, count).Select(index => $"v{index}")]
        };
        var predicate = new FilterPredicateDraft
        {
            Comparison =
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = values
            }
        };

        var component = RenderPredicate(predicate, isEditing: false);

        Assert.Contains($"[[FilterEditor_PredicateSummary(Id|[[FilterEditor_PredicateSummary_Operator_In]]|{expectedValueLabel})]]", component.Markup);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equals, MatchMode.Many, "a|b", "[[FilterEditor_PredicateSummary(TaskCategory|[[FilterEditor_PredicateSummary_Operator_In]]|[[FilterEditor_PredicateSummary_ValueCount_Many(2)]])]]")]
    [InlineData(ComparisonOperator.Contains, MatchMode.Single, "abc", "[[FilterEditor_PredicateSummary(TaskCategory|[[FilterEditor_PredicateSummary_Operator_Contains]]|abc)]]")]
    [InlineData(ComparisonOperator.NotContains, MatchMode.Single, "abc", "[[FilterEditor_PredicateSummary(TaskCategory|[[FilterEditor_PredicateSummary_Operator_NotContains]]|abc)]]")]
    [InlineData(ComparisonOperator.Equals, MatchMode.Single, "abc", "[[FilterEditor_PredicateSummary(TaskCategory|==|abc)]]")]
    [InlineData(ComparisonOperator.NotEqual, MatchMode.Single, "abc", "[[FilterEditor_PredicateSummary(TaskCategory|!=|abc)]]")]
    public void PredicateSummary_RoutesLocalizedPartsAndKeepsExpressionTokens(
        ComparisonOperator comparisonOperator,
        MatchMode matchMode,
        string value,
        string expectedSummary)
    {
        var predicate = new FilterPredicateDraft
        {
            Comparison =
            {
                Property = EventProperty.TaskCategory,
                Operator = comparisonOperator,
                MatchMode = matchMode,
                Value = value,
                Values = [.. value.Split('|')]
            }
        };

        var component = RenderPredicate(predicate, isEditing: false);

        Assert.Contains(expectedSummary, component.Markup);
        Assert.DoesNotContain("[[FilterLens_Property_TaskCategory]]", component.Markup);
    }

    [Fact]
    public void PredicateSummary_UnknownOperator_UsesRawQuestionMarkOperator()
    {
        var predicate = new FilterPredicateDraft
        {
            Comparison =
            {
                Property = EventProperty.TaskCategory,
                Operator = (ComparisonOperator)999,
                MatchMode = MatchMode.Single,
                Value = "abc"
            }
        };

        var component = RenderPredicate(predicate, isEditing: false);

        Assert.Contains("[[FilterEditor_PredicateSummary(TaskCategory|?|abc)]]", component.Markup);
        Assert.DoesNotContain("[[?]]", component.Markup);
    }

    [Fact]
    public void RowActionsScenarioCopy_RoutesNamedAndUnnamedBranchesThroughLocalizer()
    {
        var context = new ScenarioAuthoringRowContext(Enabled: true, _ => Task.CompletedTask);
        var named = Render<FilterRowActions>(parameters => parameters
            .Add(actions => actions.Value, MakeSavedFilter("Id == 1"))
            .AddCascadingValue(context));

        Assert.Contains("[[FilterEditor_RowAction_CopyScenarioAria_Named(Id == 1)]]", named.Markup);
        Assert.Contains("[[FilterEditor_RowAction_CopyScenarioTitle]]", named.Markup);

        var unnamed = Render<FilterRowActions>(parameters => parameters
            .Add(actions => actions.Value, MakeSavedFilter(string.Empty))
            .AddCascadingValue(context));

        Assert.Contains("[[FilterEditor_RowAction_CopyScenarioAria_Unnamed]]", unnamed.Markup);
    }

    [Fact]
    public void RowHeaderAndActions_RouteBothNamedAndUnnamedBranchesThroughLocalizer()
    {
        var named = Render<FilterRowShell>(parameters => parameters.Add(shell => shell.Value, MakeSavedFilter("Id == 1")));

        Assert.Contains("[[FilterEditor_RowHeader_IncludedAria(Id == 1)]]", named.Markup);
        Assert.Contains("[[FilterEditor_ExcludeToggle_TitleIncluded]]", named.Markup);
        Assert.Contains("Id == 1", named.Markup);
        Assert.Contains("[[FilterEditor_RowAction_EditAria_Named(Id == 1)]]", named.Markup);
        Assert.Contains("[[FilterEditor_RowAction_EditTitle]]", named.Markup);
        Assert.Contains("[[FilterEditor_RowAction_RemoveAria_Named(Id == 1)]]", named.Markup);
        Assert.Contains("[[FilterEditor_RowAction_RemoveTitle]]", named.Markup);
        Assert.Contains("[[FilterEditor_RowAction_EnableToggleLabel]]", named.Markup);

        var unnamed = Render<FilterRowShell>(parameters => parameters.Add(shell => shell.Value, MakeSavedFilter(string.Empty)));

        Assert.Contains("[[FilterEditor_RowHeader_NoFilterSpecified]]", unnamed.Markup);
        Assert.Contains("[[FilterEditor_RowAction_EditAria_Unnamed]]", unnamed.Markup);
        Assert.Contains("[[FilterEditor_RowAction_RemoveAria_Unnamed]]", unnamed.Markup);

        var excluded = Render<FilterRowShell>(parameters =>
            parameters.Add(shell => shell.Value, MakeSavedFilter("Id == 2", isExcluded: true)));

        Assert.Contains("[[FilterEditor_RowHeader_ExcludedAria(Id == 2)]]", excluded.Markup);
        Assert.Contains("[[FilterEditor_ExcludeToggle_TitleExcluded]]", excluded.Markup);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static async Task InvokeAsync(IRenderedComponent<FilterEditorCore> component, string methodName, params object[] arguments)
    {
        var method = typeof(FilterEditorCore).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = component.InvokeAsync(() => method.Invoke(component.Instance, arguments));
        var value = await result;

        switch (value)
        {
            case Task task:
                await task;
                break;
            case ValueTask valueTask:
                await valueTask;
                break;
        }

        component.Render();
    }

    private static IEnumerable<XElement> LoadSharedResourceData()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "src", "EventLogExpert.Localization", "Resources", "SharedResource.resx");
        return XDocument.Load(path).Root!.Elements("data");
    }

    private static SavedFilter MakeSavedFilter(string text, bool isEnabled = true, bool isExcluded = false) =>
        new()
        {
            ComparisonText = text,
            Compiled = null,
            IsEnabled = isEnabled,
            IsExcluded = isExcluded,
        };

    private static List<string> SelectedTags(IRenderedComponent<FilterEditorCore> component)
    {
        var field = typeof(FilterEditorCore).GetField(
            "_selectedTags",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var selectedTags = Assert.IsType<List<string>>(field.GetValue(component.Instance));
        return selectedTags;
    }

    private IRenderedComponent<FilterEditorCore> RenderCore(
        FilterDraft? draft = null,
        SavedFilter? value = null,
        IReadOnlyList<CachedFilterOption>? cachedOptions = null) =>
        Render<FilterEditorCore>(parameters =>
        {
            if (draft is not null)
            {
                parameters.Add(core => core.PendingDraft, draft);
            }

            if (value is not null)
            {
                parameters.Add(core => core.Value, value);
            }

            parameters.Add(core => core.CachedOptions, cachedOptions);
        });

    private IRenderedComponent<FilterPredicateEditor> RenderPredicate(FilterPredicateDraft predicate, bool isEditing) =>
        Render<FilterPredicateEditor>(parameters => parameters
            .Add(editor => editor.Value, predicate)
            .Add(editor => editor.IsEditing, isEditing));
}

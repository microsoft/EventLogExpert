// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.UI.FilterEditor.Comparison;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterEditor;

/// <summary>
///     Proves the filter value picker routes its chrome and operator strings through the localizer (via
///     <see cref="MarkerLocalizer" />) and that the ResolutionStatus present-status equality picker localizes only the
///     DISPLAY while the bound <c>Value</c>/<c>Values</c> stay the frozen tokens. Contains/NotContains remain free-text
///     verbatim.
/// </summary>
public sealed class FilterComparisonEditorLocalizerWiringTests : BunitContext
{
    private readonly IEventLogQueries _eventLogQueries = Substitute.For<IEventLogQueries>();

    public FilterComparisonEditorLocalizerWiringTests()
    {
        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(new EventLogState());
        Services.AddSingleton(eventLogState);

        _eventLogQueries.GetPropertyValues(default).ReturnsForAnyArgs(ImmutableArray<string>.Empty);
        _eventLogQueries.GetEventDataFieldNames().Returns(ImmutableArray<string>.Empty);
        _eventLogQueries.GetUserDataFieldNames().Returns(ImmutableArray<string>.Empty);
        _eventLogQueries.GetEventDataFieldValues(default!).ReturnsForAnyArgs(ImmutableArray<string>.Empty);
        _eventLogQueries.GetUserDataFieldValues(default!).ReturnsForAnyArgs(ImmutableArray<string>.Empty);
        Services.AddSingleton(_eventLogQueries);

        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddFluxor(options => options.ScanAssemblies(typeof(FilterComparisonEditor).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ChromeLabelsAndOperators_RouteThroughLocalizer()
    {
        var cut = RenderEditor(new FilterComparisonDraft { Property = EventProperty.Id });

        Assert.Contains("[[FilterEditor_PropertyLabel]]", cut.Markup);
        Assert.Contains("[[FilterEditor_ComparisonLabel]]", cut.Markup);
        Assert.Contains("[[FilterEditor_ValueLabel]]", cut.Markup);
        Assert.Contains("[[FilterEditor_AllValues]]", cut.Markup);
        Assert.Contains("[[FilterEditor_Comparison_Equals]]", cut.Markup);
        Assert.Contains("[[FilterEditor_Comparison_NotEqual]]", cut.Markup);
    }

    [Fact]
    public void EventDataProperty_RendersLocalizedFieldNameLabel()
    {
        var cut = RenderEditor(new FilterComparisonDraft { Property = EventProperty.EventData });

        Assert.Contains("[[FilterEditor_FieldNameLabel]]", cut.Markup);
    }

    [Fact]
    public void ManySelect_LocalizesDisplay_ButKeepsStoredValuesAsTokens()
    {
        var draft = new FilterComparisonDraft
        {
            Property = EventProperty.ResolutionStatus,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many,
            Values = [ResolutionStatusTokens.Failed]
        };
        _eventLogQueries.GetPropertyValues(EventProperty.ResolutionStatus)
            .Returns(ImmutableArray.Create(ResolutionStatusTokens.Failed, string.Empty));

        var cut = RenderEditor(draft);

        // The multiselect ToStringFunc localizes each token display and the empty candidate as "(Empty)"...
        Assert.Contains("[[ResolutionStatus_Failed]]", cut.Markup);
        Assert.Contains("[[FilterEditor_EmptyValuePlaceholder]]", cut.Markup);

        // ...while the stored Values stay tokens.
        Assert.Equal([ResolutionStatusTokens.Failed], draft.Values);
    }

    [Fact]
    public void ResolutionStatusContains_StaysFreeTextVerbatim_AndKeepsValue()
    {
        var draft = new FilterComparisonDraft
        {
            Property = EventProperty.ResolutionStatus,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Single,
            Value = "Res"
        };
        _eventLogQueries.GetPropertyValues(EventProperty.ResolutionStatus)
            .Returns(ImmutableArray.Create(ResolutionStatusTokens.Resolved));

        var cut = RenderEditor(draft);

        // Contains is not the closed-set branch: nothing routes through a ResolutionStatus key, and the value is verbatim.
        Assert.DoesNotContain("[[ResolutionStatus_", cut.Markup);
        Assert.Equal("Res", draft.Value);
    }

    [Fact]
    public void ResolutionStatusEquality_IsReadOnly_AndSelectingAnOption_StoresRawToken()
    {
        // Interaction guard (not just initial markup): the equality picker must be pick-only - the input is readonly so
        // no free text can be typed (this fails if IsInput is restored) - and selecting a different localized option
        // must store that option's RAW token, never its localized display. Proves the closed set AND that display
        // never leaks into storage under a real selection.
        var draft = new FilterComparisonDraft
        {
            Property = EventProperty.ResolutionStatus,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = ResolutionStatusTokens.NoProvider
        };
        _eventLogQueries.GetPropertyValues(EventProperty.ResolutionStatus)
            .Returns(ImmutableArray.Create(
                ResolutionStatusTokens.NoProvider,
                ResolutionStatusTokens.Resolved,
                ResolutionStatusTokens.Failed));

        var cut = RenderEditor(draft);

        // Pick-only: the value input is readonly, so there is no free-text entry path.
        Assert.True(cut.Find("input.filter-value-dropdown").HasAttribute("readonly"));

        // Selecting the localized "Resolved" option stores the raw token, not "[[ResolutionStatus_Resolved]]".
        cut.FindAll("div[role='option']")
            .Single(option => option.TextContent.Contains("[[ResolutionStatus_Resolved]]"))
            .MouseDown();

        Assert.Equal(ResolutionStatusTokens.Resolved, draft.Value);
    }

    [Fact]
    public void ResolutionStatusEquality_LocalizesDisplay_ButKeepsStoredValueAsToken()
    {
        var draft = new FilterComparisonDraft
        {
            Property = EventProperty.ResolutionStatus,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = ResolutionStatusTokens.NoProvider
        };
        _eventLogQueries.GetPropertyValues(EventProperty.ResolutionStatus)
            .Returns(ImmutableArray.Create(ResolutionStatusTokens.NoProvider, ResolutionStatusTokens.Resolved));

        var cut = RenderEditor(draft);

        // The DISPLAY localizes the frozen token to its own ResolutionStatus key...
        Assert.Contains("[[ResolutionStatus_NoProvider]]", cut.Markup);

        // ...while the bound Value stays the raw token, so saved filters never drift.
        Assert.Equal(ResolutionStatusTokens.NoProvider, draft.Value);
    }

    [Fact]
    public void ResolutionStatusEquality_NoSelection_HeaderShowsAll_NotEmptyPlaceholder()
    {
        // The single-select converter also drives the collapsed header. With no value chosen (null) the header must
        // mirror the "All" clear item, not the empty-status "(Empty)" placeholder, so clearing the filter never reads
        // as "filter on empty status".
        var draft = new FilterComparisonDraft
        {
            Property = EventProperty.ResolutionStatus,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = null
        };
        _eventLogQueries.GetPropertyValues(EventProperty.ResolutionStatus)
            .Returns(ImmutableArray.Create(ResolutionStatusTokens.Resolved));

        var cut = RenderEditor(draft);

        var header = cut.Find("input.filter-value-dropdown");
        Assert.Equal("[[FilterEditor_AllValues]]", header.GetAttribute("value"));
    }

    [Theory]
    [InlineData(ComparisonOperator.Equals)]
    [InlineData(ComparisonOperator.NotEqual)]
    public void ResolutionStatusEquality_OffersEveryPresentStatus_RegardlessOfCurrentValue(ComparisonOperator @operator)
    {
        // Regression: the equality picker is pick-only and always offers every ResolutionStatus token PRESENT in the
        // data, never the substring-narrowed FilteredItems used by the free-text suggestions branch. A selected value
        // must not hide the alternative statuses (which would strand the user unable to switch their pick).
        var draft = new FilterComparisonDraft
        {
            Property = EventProperty.ResolutionStatus,
            Operator = @operator,
            MatchMode = MatchMode.Single,
            Value = ResolutionStatusTokens.NoProvider
        };
        _eventLogQueries.GetPropertyValues(EventProperty.ResolutionStatus)
            .Returns(ImmutableArray.Create(
                ResolutionStatusTokens.NoProvider,
                ResolutionStatusTokens.Resolved,
                ResolutionStatusTokens.Failed));

        var cut = RenderEditor(draft);

        // Every present token's localized display is offered even though Value equals one of them - the set is the
        // full unfiltered Items, so the old Contains-narrowing that dropped Resolved/Failed cannot recur.
        Assert.Contains("[[ResolutionStatus_NoProvider]]", cut.Markup);
        Assert.Contains("[[ResolutionStatus_Resolved]]", cut.Markup);
        Assert.Contains("[[ResolutionStatus_Failed]]", cut.Markup);
    }

    [Fact]
    public void ResolutionStatusEquality_RendersEmptyTokenAsPlaceholder_NotBlankRow()
    {
        // Log-derived Items can include an empty status token; the equality picker must show it as the localized
        // "(Empty)" placeholder so it never reads as a second blank "no filter" row beside the "All" clear item.
        var draft = new FilterComparisonDraft
        {
            Property = EventProperty.ResolutionStatus,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = ResolutionStatusTokens.Resolved
        };
        _eventLogQueries.GetPropertyValues(EventProperty.ResolutionStatus)
            .Returns(ImmutableArray.Create(ResolutionStatusTokens.Resolved, string.Empty));

        var cut = RenderEditor(draft);

        Assert.Contains("[[FilterEditor_EmptyValuePlaceholder]]", cut.Markup);
    }

    private IRenderedComponent<FilterComparisonEditor> RenderEditor(FilterComparisonDraft draft) =>
        Render<FilterComparisonEditor>(parameters => parameters.Add(editor => editor.Comparison, draft));
}

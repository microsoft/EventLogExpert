// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.FilterEditor.Comparison;
using EventLogExpert.UI.FilterEditor.Editing;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class FilterPredicateEditorAccessibilityTests : BunitContext
{
    public FilterPredicateEditorAccessibilityTests()
    {
        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(new EventLogState());
        Services.AddSingleton(eventLogState);

        var eventLogQueries = Substitute.For<IEventLogQueries>();
        eventLogQueries.GetPropertyValues(default).ReturnsForAnyArgs(ImmutableArray<string>.Empty);
        Services.AddSingleton(eventLogQueries);

        Services.AddFluxor(options => options.ScanAssemblies(typeof(FilterComparisonEditor).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EditingPredicate_ComparisonLabels_UseValidScopedId_AndAriaLabelledByResolves()
    {
        var predicate = new FilterPredicateDraft();
        var expectedId = ComponentId.For(predicate.Id, ComponentIdScope.Predicate).Value;
        var comparisonLabelId = $"{expectedId}_Comparison";
        var valueLabelId = $"{expectedId}_Value";

        var component = Render<FilterPredicateEditor>(parameters => parameters
            .Add(editor => editor.Value, predicate)
            .Add(editor => editor.IsEditing, true));

        Assert.Single(component.FindAll($"#{comparisonLabelId}"));
        Assert.Single(component.FindAll($"#{valueLabelId}"));
        Assert.NotEmpty(component.FindAll($"[aria-labelledby='{comparisonLabelId}']"));
        Assert.NotEmpty(component.FindAll($"[aria-labelledby='{valueLabelId}']"));

        foreach (var element in component.FindAll("[id]"))
        {
            Assert.DoesNotContain(' ', element.GetAttribute("id")!);
        }
    }
}

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.LogTable.Histogram;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace EventLogExpert.UI.Tests.LogTable.Histogram;

[Collection(CultureSensitiveCollection.Name)]
public sealed class HistogramPaneCultureTests : BunitContext
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public HistogramPaneCultureTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Inputs/ValueSelect.razor.js");
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Histogram/HistogramPane.razor.js");

        var activeEventLog = Substitute.For<IActiveEventLogSource>();
        activeEventLog.Current.Returns((EventLogId?)null);
        var columnView = Substitute.For<IEventColumnView>();
        var viewSource = Substitute.For<IOrderedViewSource>();
        viewSource.Current.Returns(new OrderedViewPresentation(columnView, EventLogId.Create(), default, PresentationState.Current, 1));
        var dimensionRequest = Substitute.For<IHistogramDimensionRequestSource>();
        dimensionRequest.Current.Returns((HistogramDimensionRequest?)null);
        var eventFocus = Substitute.For<IEventFocusSource>();
        eventFocus.Current.Returns((SelectionEntry?)null);
        var filters = Substitute.For<IActiveFiltersSource>();
        filters.Current.Returns(ImmutableList<SavedFilter>.Empty);
        var highlightSelector = Substitute.For<IHighlightSelector>();
        highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);

        Services.AddEventLogLocalization();
        Services.AddImmediateCpuWorkScheduler();
        Services.AddSingleton(activeEventLog);
        Services.AddSingleton(viewSource);
        Services.AddSingleton(dimensionRequest);
        Services.AddSingleton(eventFocus);
        Services.AddSingleton(Substitute.For<IFilterLensCommands>());
        Services.AddSingleton(filters);
        Services.AddSingleton<IFindMarkerSource>(new FindMarkerSource());
        Services.AddSingleton(highlightSelector);
        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<ITraceLogger>());
    }

    [Fact]
    public void AxisLabels_StayFixedInvariantAcrossCultures()
    {
        string[] english = RunUnderCulture(CultureInfo.GetCultureInfo("en-US"), RenderAxisLabels);
        string[] arabic = RunUnderCulture(CultureInfo.GetCultureInfo("ar-SA"), RenderAxisLabels);

        Assert.Equal(english, arabic);
        Assert.Contains("2026-08-26 23:00", english);
        Assert.Contains("2026-08-27 01:00", english);
    }

    private static void InvokeStateHasChanged(IRenderedComponent<HistogramPane> cut)
    {
        var stateHasChanged = typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(stateHasChanged);
        cut.InvokeAsync(() => stateHasChanged.Invoke(cut.Instance, null)).GetAwaiter().GetResult();
    }

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

    private static void SetPrivateField(IRenderedComponent<HistogramPane> cut, string name, object value)
    {
        var field = typeof(HistogramPane).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(cut.Instance, value);
    }

    private string[] RenderAxisLabels()
    {
        var cut = Render<HistogramPane>();
        var start = new DateTime(2026, 8, 26, 23, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 8, 27, 1, 0, 0, DateTimeKind.Utc);
        IReadOnlyList<HistogramGroup> groups = HistogramGroups.ForCategories(["alpha"], ["Alpha"], otherLabel: null);
        var data = new HistogramData(
            [1, 1],
            1,
            2,
            start,
            end,
            2,
            TimeSpan.FromHours(1).Ticks,
            groups);
        var render = new HistogramRender(
            [
                new HistogramRenderBin(start.Ticks, start.AddHours(1).Ticks, 1, [1]),
                new HistogramRenderBin(start.AddHours(1).Ticks, end.Ticks, 1, [1])
            ],
            start.Ticks,
            end.Ticks,
            2,
            1,
            [2]);

        SetPrivateField(cut, "_viewportWidthPx", 500);
        SetPrivateField(cut, "_plotHeightPx", 100);
        SetPrivateField(cut, "_render", render);
        SetPrivateField(cut, "_baseData", data);
        SetPrivateField(cut, "_segmentHeights", new[] { 50, 50 });
        InvokeStateHasChanged(cut);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".histogram-axis-label")));
        return [.. cut.FindAll(".histogram-axis-label").Select(label => label.TextContent)];
    }
}
